﻿using System;
using System.Collections.Generic;
using System.Linq;
using NWaves.FeatureExtractors.Base;
using NWaves.Features;
using NWaves.Transforms;
using NWaves.Utils;
using NWaves.Windows;

namespace NWaves.FeatureExtractors.Multi
{
    /// <summary>
    /// Extractor of spectral features.
    /// It's a flexible extractor that allows setting frequencies of interest.
    /// At least one spectral feature MUST be specified.
    /// </summary>
    public class SpectralFeaturesExtractor : FeatureExtractor
    {
        /// <summary>
        /// Names of supported spectral features
        /// </summary>
        public const string FeatureSet = "centroid, spread, flatness, noiseness, rolloff, crest, entropy, decrease, c1+c2+c3+c4+c5+c6";

        /// <summary>
        /// String annotations (or simply names) of features
        /// </summary>
        public override List<string> FeatureDescriptions { get; }

        /// <summary>
        /// Number of features to extract
        /// </summary>
        public override int FeatureCount => FeatureDescriptions.Count;

        /// <summary>
        /// Extractor functions
        /// </summary>
        protected List<Func<float[], float[], float>> _extractors;

        /// <summary>
        /// Extractor parameters
        /// </summary>
        protected readonly IReadOnlyDictionary<string, object> _parameters;

        /// <summary>
        /// FFT transformer
        /// </summary>
        protected readonly RealFft _fft;

        /// <summary>
        /// Center frequencies (uniform in Herz scale by default; could be uniform in mel-scale or octave-scale, for example)
        /// </summary>
        protected readonly float[] _frequencies;

        /// <summary>
        /// Internal buffer for magnitude spectrum
        /// </summary>
        protected readonly float[] _spectrum;

        /// <summary>
        /// Internal buffer for magnitude spectrum taken only at frequencies of interest
        /// </summary>
        protected float[] _mappedSpectrum;

        /// <summary>
        /// Internal buffer for spectral positions of frequencies of interest
        /// </summary>
        protected readonly int[] _frequencyPositions;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="samplingRate"></param>
        /// <param name="featureList"></param>
        /// <param name="frameDuration"></param>
        /// <param name="hopDuration"></param>
        /// <param name="fftSize"></param>
        /// <param name="parameters"></param>
        public SpectralFeaturesExtractor(int samplingRate,
                                         string featureList,
                                         double frameDuration = 0.0256/*sec*/,
                                         double hopDuration = 0.010/*sec*/,
                                         int fftSize = 0,
                                         float[] frequencies = null,
                                         double preEmphasis = 0,
                                         WindowTypes window = WindowTypes.Hamming,
                                         IReadOnlyDictionary<string, object> parameters = null)

            : base(samplingRate, frameDuration, hopDuration, preEmphasis, window)
        {
            if (featureList == "all" || featureList == "full")
            {
                featureList = FeatureSet;
            }

            var features = featureList.Split(',', '+', '-', ';', ':')
                                      .Select(f => f.Trim().ToLower());

            _extractors = features.Select<string, Func<float[], float[], float>>(feature =>
            {
                switch (feature)
                {
                    case "sc":
                    case "centroid":
                        return Spectral.Centroid;

                    case "ss":
                    case "spread":
                        return Spectral.Spread;

                    case "sfm":
                    case "flatness":
                        if (parameters?.ContainsKey("minLevel") ?? false)
                        {
                            var minLevel = (float)parameters["minLevel"];
                            return (spectrum, freqs) => Spectral.Flatness(spectrum, minLevel);
                        }
                        else
                        {
                            return (spectrum, freqs) => Spectral.Flatness(spectrum);
                        }

                    case "sn":
                    case "noiseness":
                        if (parameters?.ContainsKey("noiseFrequency") ?? false)
                        {
                            var noiseFrequency = (float)parameters["noiseFrequency"];
                            return (spectrum, freqs) => Spectral.Noiseness(spectrum, freqs, noiseFrequency);
                        }
                        else
                        {
                            return (spectrum, freqs) => Spectral.Noiseness(spectrum, freqs);
                        }

                    case "rolloff":
                        if (parameters?.ContainsKey("rolloffPercent") ?? false)
                        {
                            var rolloffPercent = (float)parameters["rolloffPercent"];
                            return (spectrum, freqs) => Spectral.Rolloff(spectrum, freqs, rolloffPercent);
                        }
                        else
                        {
                            return (spectrum, freqs) => Spectral.Rolloff(spectrum, freqs);
                        }

                    case "crest":
                        return (spectrum, freqs) => Spectral.Crest(spectrum);

                    case "entropy":
                    case "ent":
                        return (spectrum, freqs) => Spectral.Entropy(spectrum);

                    case "sd":
                    case "decrease":
                        return (spectrum, freqs) => Spectral.Decrease(spectrum);

                    case "c1":
                    case "c2":
                    case "c3":
                    case "c4":
                    case "c5":
                    case "c6":
                        return (spectrum, freqs) => Spectral.Contrast(spectrum, freqs, int.Parse(feature.Substring(1)));

                    default:
                        return (spectrum, freqs) => 0;
                }
            }).ToList();

            FeatureDescriptions = features.ToList();
            
            _blockSize = fftSize > FrameSize ? fftSize : MathUtils.NextPowerOfTwo(FrameSize);
            _fft = new RealFft(_blockSize);

            var resolution = (float)samplingRate / _blockSize;

            if (frequencies == null)
            {
                _frequencies = Enumerable.Range(0, _blockSize / 2 + 1)
                                         .Select(f => f * resolution)
                                         .ToArray();
            }
            else if (frequencies.Length == _blockSize / 2 + 1)
            {
                _frequencies = frequencies;
            }
            else
            {
                _frequencies = new float[frequencies.Length + 1];
                frequencies.FastCopyTo(_frequencies, frequencies.Length, 0, 1);
                _mappedSpectrum = new float[_frequencies.Length];
                _frequencyPositions = new int[_frequencies.Length];

                for (var i = 1; i < _frequencies.Length; i++)
                {
                    _frequencyPositions[i] = (int)(_frequencies[i] / resolution) + 1;
                }
            }

            _parameters = parameters;

            _spectrum = new float[_blockSize / 2 + 1];  // buffer for magnitude spectrum
        }

        /// <summary>
        /// Add one more feature with routine for its calculation
        /// </summary>
        /// <param name="name"></param>
        /// <param name="algorithm"></param>
        public void AddFeature(string name, Func<float[], float[], float> algorithm)
        {
            FeatureDescriptions.Insert(_extractors.Count, name);
            _extractors.Add(algorithm);
        }

        /// <summary>
        /// Compute spectral features in one frame
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        public override float[] ProcessFrame(float[] block)
        {
            // compute and prepare spectrum

            _fft.MagnitudeSpectrum(block, _spectrum);

            var featureVector = new float[FeatureCount];

            if (_spectrum.Length == _frequencies.Length)
            {
                _mappedSpectrum = _spectrum;
            }
            else
            {
                for (var j = 0; j < _mappedSpectrum.Length; j++)
                {
                    _mappedSpectrum[j] = _spectrum[_frequencyPositions[j]];
                }
            }

            // extract spectral features

            for (var j = 0; j < _extractors.Count; j++)
            {
                featureVector[j] = _extractors[j](_mappedSpectrum, _frequencies);
            }

            return featureVector;
        }

        /// <summary>
        /// True if computations can be done in parallel
        /// </summary>
        /// <returns></returns>
        public override bool IsParallelizable() => true;

        /// <summary>
        /// Copy of current extractor that can work in parallel
        /// </summary>
        /// <returns></returns>
        public override FeatureExtractor ParallelCopy()
        {
            var spectralFeatureSet = string.Join(",", FeatureDescriptions.Take(_extractors.Count));
            
            var copy = new SpectralFeaturesExtractor(SamplingRate, spectralFeatureSet, FrameDuration, HopDuration, _blockSize, _frequencies, _preEmphasis, _window, _parameters)
            {
                _extractors = _extractors
            };

            return copy;
        }
    }
}
