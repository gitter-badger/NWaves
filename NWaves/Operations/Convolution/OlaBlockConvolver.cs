﻿using System;
using System.Collections.Generic;
using System.Linq;
using NWaves.Filters.Base;
using NWaves.Signals;
using NWaves.Transforms;
using NWaves.Utils;

namespace NWaves.Operations.Convolution
{
    /// <summary>
    /// Class responsible for OLA block convolution.
    /// It can be used as a filter (online filter as well).
    /// </summary>
    public class OlaBlockConvolver : IFilter, IOnlineFilter
    {
        /// <summary>
        /// Filter kernel
        /// </summary>
        private readonly float[] _kernel;

        /// <summary>
        /// FFT size (also the size of one analyzed chunk)
        /// </summary>
        private readonly int _fftSize;

        /// <summary>
        /// FFT transformer
        /// </summary>
        private readonly RealFft _fft;

        /// <summary>
        /// Offset in the input delay line
        /// </summary>
        private int _bufferOffset;

        /// <summary>
        /// Offset in the delay line
        /// </summary>
        private int _outputBufferOffset;

        /// <summary>
        /// internal buffers
        /// </summary>
        private readonly float[] _kernelSpectrumRe;
        private readonly float[] _kernelSpectrumIm;
        private readonly float[] _blockRe;
        private readonly float[] _blockIm;
        private readonly float[] _convRe;
        private readonly float[] _convIm;
        private readonly float[] _lastSaved;

        /// <summary>
        /// Hop size
        /// </summary>
        public int HopSize => _fftSize - _kernel.Length + 1;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="kernel"></param>
        /// <param name="fftSize"></param>
        public OlaBlockConvolver(IEnumerable<float> kernel, int fftSize)
        {
            _fftSize = MathUtils.NextPowerOfTwo(fftSize);

            if (kernel.Count() > _fftSize)
            {
                throw new ArgumentException("Kernel length must not exceed the size of FFT!");
            }

            _fft = new RealFft(_fftSize);

            _kernel = kernel.ToArray();
            _kernelSpectrumRe = _kernel.PadZeros(_fftSize);
            _kernelSpectrumIm = new float[_fftSize];
            _convRe = new float[_fftSize];
            _convIm = new float[_fftSize];
            _blockRe = new float[_fftSize];
            _blockIm = new float[_fftSize];
            _lastSaved = new float[_kernel.Length - 1];

            _fft.Direct(_kernelSpectrumRe, _kernelSpectrumRe, _kernelSpectrumIm);

            Reset();
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="kernel"></param>
        /// <param name="fftSize"></param>
        public OlaBlockConvolver(IEnumerable<double> kernel, int fftSize) : this(kernel.ToFloats(), fftSize)
        {
        }

        /// <summary>
        /// Construct BlockConvolver from a specific FIR filter
        /// </summary>
        /// <param name="filter"></param>
        /// <param name="fftSize"></param>
        /// <returns></returns>
        public static OlaBlockConvolver FromFilter(FirFilter filter, int fftSize)    
        {
            fftSize = MathUtils.NextPowerOfTwo(fftSize);
            return new OlaBlockConvolver(filter.Kernel, fftSize);
        }

        /// <summary>
        /// OLA online filtering (sample-by-sample)
        /// </summary>
        /// <param name="sample"></param>
        /// <returns></returns>
        public float Process(float sample)
        {
            _blockRe[_bufferOffset++] = sample;

            if (_bufferOffset == HopSize)
            {
                ProcessFrame();
            }

            return _convRe[_outputBufferOffset++];
        }

        /// <summary>
        /// Process one frame (block)
        /// </summary>
        public void ProcessFrame()
        {
            var M = _kernel.Length;

            var halfSize = _fftSize / 2;

            Array.Clear(_blockIm, 0, _fftSize);
            Array.Clear(_blockRe, HopSize, M - 1);

            _fft.Direct(_blockRe, _blockRe, _blockIm);
            for (var j = 0; j <= halfSize; j++)
            {
                _convRe[j] = (_blockRe[j] * _kernelSpectrumRe[j] - _blockIm[j] * _kernelSpectrumIm[j]) / halfSize;
                _convIm[j] = (_blockRe[j] * _kernelSpectrumIm[j] + _blockIm[j] * _kernelSpectrumRe[j]) / halfSize;
            }
            _fft.Inverse(_convRe, _convIm, _convRe);

            for (var j = 0; j < M - 1; j++)
            {
                _convRe[j] += _lastSaved[j];
            }

            _convRe.FastCopyTo(_lastSaved, M - 1, HopSize);

            _outputBufferOffset = 0;
            _bufferOffset = 0;
        }

        /// <summary>
        /// Offline OLA filtering
        /// </summary>
        /// <param name="signal"></param>
        /// <param name="method"></param>
        /// <returns></returns>
        public DiscreteSignal ApplyTo(DiscreteSignal signal, FilteringMethod method = FilteringMethod.Auto)
        {
            var firstCount = Math.Min(HopSize - 1, signal.Length);

            int i = 0, j = 0;

            for (; i < firstCount; i++)    // first HopSize-1 samples are just placed in the delay line
            {
                Process(signal[i]);
            }

            var filtered = new float[signal.Length + _kernel.Length - 1];

            for (; i < signal.Length; i++, j++)    // process
            {
                filtered[j] = Process(signal[i]);
            }

            var lastCount = firstCount + _kernel.Length - 1;

            for (i = 0; i < lastCount; i++, j++)    // get last 'late' samples
            {
                filtered[j] = Process(0.0f);
            }

            return new DiscreteSignal(signal.SamplingRate, filtered);
        }

        /// <summary>
        /// Reset filter internals
        /// </summary>
        public void Reset()
        {
            _bufferOffset = 0;
            _outputBufferOffset = 0;

            Array.Clear(_lastSaved, 0, _lastSaved.Length);
            Array.Clear(_blockRe, 0, _blockRe.Length);
            Array.Clear(_blockIm, 0, _blockIm.Length);
            Array.Clear(_convRe, 0, _convRe.Length);
            Array.Clear(_convIm, 0, _convIm.Length);
        }
    }
}
