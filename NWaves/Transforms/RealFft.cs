﻿using NWaves.Signals;
using NWaves.Utils;
using System;

namespace NWaves.Transforms
{
    /// <summary>
    /// FFT transformer for real inputs
    /// </summary>
    public class RealFft
    {
        /// <summary>
        /// Size of FFT
        /// </summary>
        public int Size => _fftSize * 2;

        /// <summary>
        /// Half of FFT size (for calculations)
        /// </summary>
        private int _fftSize;

        /// <summary>
        /// Precomputed cosines
        /// </summary>
        private readonly float[] _cosTbl;

        /// <summary>
        /// Precomputed sines
        /// </summary>
        private readonly float[] _sinTbl;

        /// <summary>
        /// Precomputed coefficients
        /// </summary>
        private float[] _ar, _br, _ai, _bi;

        /// <summary>
        /// Internal buffers
        /// </summary>
        private readonly float[] _re;
        private readonly float[] _im;
        private readonly float[] _realSpectrum;
        private readonly float[] _imagSpectrum;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="size"></param>
        public RealFft(int size)
        {
            Guard.AgainstNotPowerOfTwo(size, "Size of FFT");

            _fftSize = size / 2;

            _re = new float[_fftSize];
            _im = new float[_fftSize];

            _realSpectrum = new float[_fftSize + 1];
            _imagSpectrum = new float[_fftSize + 1];

            // precompute coefficients:

            var tblSize = (int)Math.Log(_fftSize, 2);

            _cosTbl = new float[tblSize];
            _sinTbl = new float[tblSize];

            for (int i = 1, pos = 0; i < _fftSize; i *= 2, pos++)
            {
                _cosTbl[pos] = (float)Math.Cos(2 * Math.PI * i / _fftSize);
                _sinTbl[pos] = (float)Math.Sin(2 * Math.PI * i / _fftSize);
            }

            _ar = new float[_fftSize];
            _br = new float[_fftSize];
            _ai = new float[_fftSize];
            _bi = new float[_fftSize];

            var f = Math.PI / _fftSize;

            for (var i = 0; i < _fftSize; i++)
            {
                _ar[i] = (float)(0.5 * (1 - Math.Sin(f * i)));
                _ai[i] = (float)(-0.5 * Math.Cos(f * i));
                _br[i] = (float)(0.5 * (1 + Math.Sin(f * i)));
                _bi[i] = (float)(0.5 * Math.Cos(f * i));
            }
        }

        /// <summary>
        /// Direct transform
        /// </summary>
        /// <param name="input"></param>
        /// <param name="re"></param>
        /// <param name="im"></param>
        public void Direct(float[] input, float[] re, float[] im)
        {
            // do half-size complex FFT:

            for (int i = 0, k = 0; i < _fftSize; i++)
            {
                _re[i] = input[k++];
                _im[i] = input[k++];
            }

            var L = _fftSize;
            var M = _fftSize >> 1;
            var S = _fftSize - 1;
            var ti = 0;
            while (L >= 2)
            {
                var l = L >> 1;
                var u1 = 1.0f;
                var u2 = 0.0f;
                var c = _cosTbl[ti];
                var s = -_sinTbl[ti];
                ti++;
                for (var j = 0; j < l; j++)
                {
                    for (var i = j; i < _fftSize; i += L)
                    {
                        var p = i + l;
                        var t1 = _re[i] + _re[p];
                        var t2 = _im[i] + _im[p];
                        var t3 = _re[i] - _re[p];
                        var t4 = _im[i] - _im[p];
                        _re[p] = t3 * u1 - t4 * u2;
                        _im[p] = t4 * u1 + t3 * u2;
                        _re[i] = t1;
                        _im[i] = t2;
                    }
                    var u3 = u1 * c - u2 * s;
                    u2 = u2 * c + u1 * s;
                    u1 = u3;
                }
                L >>= 1;
            }
            for (int i = 0, j = 0; i < S; i++)
            {
                if (i > j)
                {
                    var t1 = _re[j];
                    var t2 = _im[j];
                    _re[j] = _re[i];
                    _im[j] = _im[i];
                    _re[i] = t1;
                    _im[i] = t2;
                }
                var k = M;
                while (j >= k)
                {
                    j -= k;
                    k >>= 1;
                }
                j += k;
            }

            // do the last step:

            re[0] = _re[0] * _ar[0] - _im[0] * _ai[0] + _re[0] * _br[0] + _im[0] * _bi[0];
            im[0] = _im[0] * _ar[0] + _re[0] * _ai[0] + _re[0] * _bi[0] - _im[0] * _br[0];

            for (var k = 1; k < _fftSize; k++)
            {
                re[k] = _re[k] * _ar[k] - _im[k] * _ai[k] + _re[_fftSize - k] * _br[k] + _im[_fftSize - k] * _bi[k];
                im[k] = _im[k] * _ar[k] + _re[k] * _ai[k] + _re[_fftSize - k] * _bi[k] - _im[_fftSize - k] * _br[k];
            }

            re[_fftSize] = _re[0] - _im[0];
            im[_fftSize] = 0;
        }

        /// <summary>
        /// Inverse transform
        /// </summary>
        /// <param name="re"></param>
        /// <param name="im"></param>
        /// <param name="output"></param>
        public void Inverse(float[] re, float[] im, float[] output)
        {
            // do the first step:

            for (var k = 0; k < _fftSize; k++)
            {
                _re[k] = re[k] * _ar[k] + im[k] * _ai[k] + re[_fftSize - k] * _br[k] - im[_fftSize - k] * _bi[k];
                _im[k] = im[k] * _ar[k] - re[k] * _ai[k] - re[_fftSize - k] * _bi[k] - im[_fftSize - k] * _br[k];
            }

            // do half-size complex FFT:

            var L = _fftSize;
            var M = _fftSize >> 1;
            var S = _fftSize - 1;
            var ti = 0;
            while (L >= 2)
            {
                var l = L >> 1;
                var u1 = 1.0f;
                var u2 = 0.0f;
                var c = _cosTbl[ti];
                var s = _sinTbl[ti];
                ti++;
                for (var j = 0; j < l; j++)
                {
                    for (var i = j; i < _fftSize; i += L)
                    {
                        var p = i + l;
                        var t1 = _re[i] + _re[p];
                        var t2 = _im[i] + _im[p];
                        var t3 = _re[i] - _re[p];
                        var t4 = _im[i] - _im[p];
                        _re[p] = t3 * u1 - t4 * u2;
                        _im[p] = t4 * u1 + t3 * u2;
                        _re[i] = t1;
                        _im[i] = t2;
                    }
                    var u3 = u1 * c - u2 * s;
                    u2 = u2 * c + u1 * s;
                    u1 = u3;
                }
                L >>= 1;
            }
            for (int i = 0, j = 0; i < S; i++)
            {
                if (i > j)
                {
                    var t1 = _re[j];
                    var t2 = _im[j];
                    _re[j] = _re[i];
                    _im[j] = _im[i];
                    _re[i] = t1;
                    _im[i] = t2;
                }
                var k = M;
                while (j >= k)
                {
                    j -= k;
                    k >>= 1;
                }
                j += k;
            }

            // fill output:

            for (int i = 0, k = 0; i < _fftSize; i++)
            {
                output[k++] = _re[i];
                output[k++] = _im[i];
            }
        }

        /// <summary>
        /// Magnitude spectrum:
        /// 
        ///     spectrum = sqrt(re * re + im * im)
        /// 
        /// Since for realFFT: im[0] = im[fftSize/2] = 0
        /// we don't process separately these elements (like in case of FFT)
        /// 
        /// </summary>
        /// <param name="samples">Array of samples (samples parts)</param>
        /// <param name="spectrum">Magnitude spectrum</param>
        /// <param name="normalize">Normalization flag</param>
        public void MagnitudeSpectrum(float[] samples, float[] spectrum, bool normalize = false)
        {
            Direct(samples, _realSpectrum, _imagSpectrum);

            if (normalize)
            {
                for (var i = 0; i < spectrum.Length; i++)
                {
                    spectrum[i] = (float)(Math.Sqrt(_realSpectrum[i] * _realSpectrum[i] + _imagSpectrum[i] * _imagSpectrum[i]) / _fftSize);
                }
            }
            else
            {
                for (var i = 0; i < spectrum.Length; i++)
                {
                    spectrum[i] = (float)(Math.Sqrt(_realSpectrum[i] * _realSpectrum[i] + _imagSpectrum[i] * _imagSpectrum[i]));
                }
            }
        }

        /// <summary>
        /// Power spectrum (normalized by default):
        /// 
        ///     spectrum =   (re * re + im * im) / fftSize
        /// 
        /// Since for realFFT: im[0] = im[fftSize/2] = 0
        /// we don't process separately these elements (like in case of FFT)
        /// 
        /// </summary>
        /// <param name="samples">Array of samples (samples parts)</param>
        /// <param name="spectrum">Power spectrum</param>
        /// <param name="normalize">Normalization flag</param>
        public void PowerSpectrum(float[] samples, float[] spectrum, bool normalize = true)
        {
            Direct(samples, _realSpectrum, _imagSpectrum);

            if (normalize)
            {
                for (var i = 0; i < spectrum.Length; i++)
                {
                    spectrum[i] = (_realSpectrum[i] * _realSpectrum[i] + _imagSpectrum[i] * _imagSpectrum[i]) / _fftSize;
                }
            }
            else
            {
                for (var i = 0; i < spectrum.Length; i++)
                {
                    spectrum[i] = _realSpectrum[i] * _realSpectrum[i] + _imagSpectrum[i] * _imagSpectrum[i];
                }
            }
        }

        /// <summary>
        /// Overloaded method for DiscreteSignal as an input
        /// </summary>
        /// <param name="signal"></param>
        /// <param name="normalize"></param>
        /// <returns></returns>
        public DiscreteSignal MagnitudeSpectrum(DiscreteSignal signal, bool normalize = false)
        {
            var spectrum = new float[_fftSize + 1];
            MagnitudeSpectrum(signal.Samples, spectrum, normalize);
            return new DiscreteSignal(signal.SamplingRate, spectrum);
        }

        /// <summary>
        /// Overloaded method for DiscreteSignal as an input
        /// </summary>
        /// <param name="signal"></param>
        /// <param name="normalize"></param>
        /// <returns></returns>
        public DiscreteSignal PowerSpectrum(DiscreteSignal signal, bool normalize = true)
        {
            var spectrum = new float[_fftSize + 1];
            PowerSpectrum(signal.Samples, spectrum, normalize);
            return new DiscreteSignal(signal.SamplingRate, spectrum);
        }

        /// <summary>
        /// FFT shift (in-place)
        /// </summary>
        /// <param name="samples"></param>
        public static void Shift(float[] samples)
        {
            if ((samples.Length & 1) == 1)
            {
                throw new ArgumentException("FFT shift is not supported for arrays with odd lengths");
            }

            var mid = samples.Length / 2;

            for (var i = 0; i < samples.Length / 2; i++)
            {
                var shift = i + mid;
                var tmp = samples[i];
                samples[i] = samples[shift];
                samples[shift] = tmp;
            }
        }
    }
}
