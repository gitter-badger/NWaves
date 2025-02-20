﻿using System;
using System.Collections.Generic;
using System.Linq;
using NWaves.FeatureExtractors.Base;
using NWaves.Filters.Fda;
using NWaves.Transforms;
using NWaves.Utils;
using NWaves.Windows;

namespace NWaves.FeatureExtractors
{
    /// <summary>
    /// Mel Frequency Cepstral Coefficients extractor.
    /// 
    /// Since so many variations of MFCC have been developed since 1980,
    /// this class is very general and allows customizing pretty everything:
    /// 
    ///  - filterbank (by default it's MFCC-FB24 HTK/Kaldi-style)
    ///  
    ///  - non-linearity type (logE, log10, decibel (librosa power_to_db analog), cubic root)
    ///  
    ///  - spectrum calculation type (power/magnitude normalized/not normalized)
    ///  
    ///  - DCT type (1,2,3,4 normalized or not): "1", "1N", "2", "2N", etc.
    ///  
    ///  - floor value for LOG-calculations (usually it's float.Epsilon; HTK default seems to be 1.0 and in librosa 1e-10 is used)
    /// 
    /// </summary>
    public class MfccExtractor : FeatureExtractor
    {
        /// <summary>
        /// Number of coefficients (including coeff #0)
        /// </summary>
        public override int FeatureCount { get; }

        /// <summary>
        /// Descriptions (simply "mfcc0", "mfcc1", "mfcc2", etc.)
        /// </summary>
        public override List<string> FeatureDescriptions =>
            Enumerable.Range(0, FeatureCount).Select(i => "mfcc" + i).ToList();

        /// <summary>
        /// Filterbank matrix of dimension [filterbankSize * (fftSize/2 + 1)].
        /// By default it's mel filterbank.
        /// </summary>
        public float[][] FilterBank { get; }

        /// <summary>
        /// Lower frequency (Hz)
        /// </summary>
        protected readonly double _lowFreq;

        /// <summary>
        /// Upper frequency (Hz)
        /// </summary>
        protected readonly double _highFreq;

        /// <summary>
        /// Size of liftering window
        /// </summary>
        protected readonly int _lifterSize;

        /// <summary>
        /// Liftering window coefficients
        /// </summary>
        protected readonly float[] _lifterCoeffs;

        /// <summary>
        /// FFT transformer
        /// </summary>
        protected readonly RealFft _fft;

        /// <summary>
        /// DCT-II transformer
        /// </summary>
        protected readonly IDct _dct;

        /// <summary>
        /// DCT type ("1", "1N", "2", "2N", "3", "3N", "4", "4N")
        /// </summary>
        protected readonly string _dctType;

        /// <summary>
        /// Non-linearity type (logE, log10, decibel, cubic root)
        /// </summary>
        protected readonly NonLinearityType _nonLinearityType;

        /// <summary>
        /// Spectrum calculation scheme (power/magnitude normalized/not normalized)
        /// </summary>
        protected readonly SpectrumType _spectrumType;

        /// <summary>
        /// Should the first MFCC coefficient be replaced with LOG(energy)
        /// </summary>
        protected readonly bool _includeEnergy;

        /// <summary>
        /// Floor value for LOG calculations
        /// </summary>
        protected readonly float _logFloor;

        /// <summary>
        /// Delegate for calculating spectrum
        /// </summary>
        protected readonly Action<float[]> _getSpectrum;

        /// <summary>
        /// Delegate for post-processing spectrum
        /// </summary>
        protected readonly Action _postProcessSpectrum;

        /// <summary>
        /// Delegate for applying DCT
        /// </summary>
        protected readonly Action<float[]> _applyDct;
        
        /// <summary>
        /// Internal buffer for a signal spectrum at each step
        /// </summary>
        protected readonly float[] _spectrum;

        /// <summary>
        /// Internal buffer for a post-processed mel-spectrum at each step
        /// </summary>
        protected readonly float[] _melSpectrum;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="samplingRate"></param>
        /// <param name="featureCount"></param>
        /// <param name="frameDuration"></param>
        /// <param name="hopDuration"></param>
        /// <param name="filterbankSize"></param>
        /// <param name="lowFreq"></param>
        /// <param name="highFreq"></param>
        /// <param name="fftSize"></param>
        /// <param name="filterbank"></param>
        /// <param name="lifterSize"></param>
        /// <param name="preEmphasis"></param>
        /// <param name="includeEnergy"></param>
        /// <param name="dctType">"1", "1N", "2", "2N", "3", "3N", "4", "4N"</param>
        /// <param name="nonLinearity"></param>
        /// <param name="spectrumType"></param>
        /// <param name="window"></param>
        /// <param name="logFloor"></param>
        public MfccExtractor(int samplingRate,
                             int featureCount,
                             double frameDuration = 0.0256/*sec*/,
                             double hopDuration = 0.010/*sec*/,
                             int filterbankSize = 24,
                             double lowFreq = 0,
                             double highFreq = 0,
                             int fftSize = 0,
                             float[][] filterbank = null,
                             int lifterSize = 0,
                             double preEmphasis = 0,
                             bool includeEnergy = false,
                             string dctType = "2N",
                             NonLinearityType nonLinearity = NonLinearityType.Log10,
                             SpectrumType spectrumType = SpectrumType.Power,
                             WindowTypes window = WindowTypes.Hamming,
                             float logFloor = float.Epsilon)

            : base(samplingRate, frameDuration, hopDuration, preEmphasis, window)
        {
            FeatureCount = featureCount;

            _lowFreq = lowFreq;
            _highFreq = highFreq;

            if (filterbank == null)
            {
                _blockSize = fftSize > FrameSize ? fftSize : MathUtils.NextPowerOfTwo(FrameSize);

                var melBands = FilterBanks.MelBands(filterbankSize, _blockSize, SamplingRate, _lowFreq, _highFreq);
                FilterBank = FilterBanks.Triangular(_blockSize, SamplingRate, melBands, mapper: Scale.HerzToMel);   // HTK/Kaldi-style
            }
            else
            {
                FilterBank = filterbank;
                filterbankSize = filterbank.Length;
                _blockSize = 2 * (filterbank[0].Length - 1);

                Guard.AgainstExceedance(FrameSize, _blockSize, "frame size", "FFT size");
            }

            _fft = new RealFft(_blockSize);

            _lifterSize = lifterSize;
            _lifterCoeffs = _lifterSize > 0 ? Window.Liftering(FeatureCount, _lifterSize) : null;

            _includeEnergy = includeEnergy;

            // setup DCT: ============================================================================

            _dctType = dctType;
            switch (dctType[0])
            {
                case '1':
                    _dct = new Dct1(filterbankSize);
                    break;
                case '2':
                    _dct = new Dct2(filterbankSize);
                    break;
                case '3':
                    _dct = new Dct3(filterbankSize);
                    break;
                case '4':
                    _dct = new Dct4(filterbankSize);
                    break;
                default:
                    throw new ArgumentException("Only DCT-1, 2, 3 and 4 are supported!");
            }

            if (dctType.Length > 1 && char.ToUpper(dctType[1]) == 'N')
            {
                _applyDct = mfccs => _dct.DirectNorm(_melSpectrum, mfccs);
            }
            else
            {
                _applyDct = mfccs => _dct.Direct(_melSpectrum, mfccs);
            }

            // setup spectrum post-processing: =======================================================

            _logFloor = logFloor;
            _nonLinearityType = nonLinearity;
            switch (nonLinearity)
            {
                case NonLinearityType.Log10:
                    _postProcessSpectrum = () => FilterBanks.ApplyAndLog10(FilterBank, _spectrum, _melSpectrum, _logFloor);
                    break;
                case NonLinearityType.LogE:
                    _postProcessSpectrum = () => FilterBanks.ApplyAndLog(FilterBank, _spectrum, _melSpectrum, _logFloor);
                    break;
                case NonLinearityType.ToDecibel:
                    _postProcessSpectrum = () => FilterBanks.ApplyAndToDecibel(FilterBank, _spectrum, _melSpectrum, _logFloor);
                    break;
                case NonLinearityType.CubicRoot:
                    _postProcessSpectrum = () => FilterBanks.ApplyAndPow(FilterBank, _spectrum, _melSpectrum, 0.33);
                    break;
                default:
                    _postProcessSpectrum = () => FilterBanks.Apply(FilterBank, _spectrum, _melSpectrum);
                    break;
            }

            _spectrumType = spectrumType;
            switch (_spectrumType)
            {
                case SpectrumType.Magnitude:
                    _getSpectrum = block => _fft.MagnitudeSpectrum(block, _spectrum, false);
                    break;
                case SpectrumType.Power:
                    _getSpectrum = block => _fft.PowerSpectrum(block, _spectrum, false);
                    break;
                case SpectrumType.MagnitudeNormalized:
                    _getSpectrum = block => _fft.MagnitudeSpectrum(block, _spectrum, true);
                    break;
                case SpectrumType.PowerNormalized:
                    _getSpectrum = block => _fft.PowerSpectrum(block, _spectrum, true);
                    break;
            }

            // reserve memory for reusable blocks

            _spectrum = new float[_blockSize / 2 + 1];
            _melSpectrum = new float[filterbankSize];
        }


        /// <summary>
        /// Standard method for computing MFCC features.
        /// According to default configuration, in each frame do:
        /// 
        ///     0) Apply window (base extractor does it)
        ///     1) Obtain power spectrum X
        ///     2) Apply mel filters and log() the result: Y = Log(X * H)
        ///     3) Do dct: mfcc = Dct(Y)
        ///     4) [Optional] liftering of mfcc
        /// 
        /// </summary>
        /// <param name="block">Samples for analysis</param>
        /// <returns>MFCC vector</returns>
        public override float[] ProcessFrame(float[] block)
        {
            // 1) calculate magnitude/power spectrum (with/without normalization)

            _getSpectrum(block);        //  block -> _spectrum

            // 2) apply mel filterbank and take log10/ln/cubic_root of the result

            _postProcessSpectrum();     // _spectrum -> _melSpectrum

            // 3) dct

            var mfccs = new float[FeatureCount];

            _applyDct(mfccs);           // _melSpectrum -> mfccs


            // 4) (optional) liftering

            if (_lifterCoeffs != null)
            {
                mfccs.ApplyWindow(_lifterCoeffs);
            }

            // 5) (optional) replace first coeff with log(energy) 

            if (_includeEnergy)
            {
                mfccs[0] = (float)(Math.Log(block.Sum(x => x * x)));
            }

            return mfccs;
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
        public override FeatureExtractor ParallelCopy() =>
            new MfccExtractor( SamplingRate, 
                               FeatureCount,
                               FrameDuration, 
                               HopDuration,
                               FilterBank.Length, 
                              _lowFreq,
                              _highFreq,
                              _blockSize, 
                               FilterBank, 
                              _lifterSize, 
                              _preEmphasis,
                              _includeEnergy,
                              _dctType,
                              _nonLinearityType,
                              _spectrumType,
                              _window,
                              _logFloor);
    }
}
