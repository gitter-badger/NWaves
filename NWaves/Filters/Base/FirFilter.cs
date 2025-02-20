﻿using System.Collections.Generic;
using System.Linq;
using NWaves.Operations.Convolution;
using NWaves.Signals;
using NWaves.Utils;

namespace NWaves.Filters.Base
{
    /// <summary>
    /// Class representing Finite Impulse Response filters
    /// </summary>
    public class FirFilter : LtiFilter
    {
        /// <summary>
        /// Filter's kernel.
        /// 
        /// Numerator part coefficients in filter's transfer function 
        /// (non-recursive part in difference equations).
        /// 
        /// Since the number of coefficients can be really big,
        /// we store ONLY float versions and they are used for filtering.
        /// 
        /// For design & analysis use the transfer function (Tf property, set via constructor).
        /// By default Tf is null, so if you need your FIR filter to do just filtering, you won't waste RAM.
        /// 
        /// </summary>
        public float[] Kernel => _kernel;
        protected float[] _kernel;

        /// <summary>
        /// Transfer function (created lazily or set specifically if needed)
        /// </summary>
        protected TransferFunction _tf;
        public override TransferFunction Tf
        {
            get => _tf ?? new TransferFunction(_kernel.ToDoubles(), new[] { 1.0 });
            protected set => _tf = value;
        }

        /// <summary>
        /// If _kernel.Length exceeds this value, 
        /// the filtering code will always call Overlap-Add routine.
        /// </summary>
        public const int FilterSizeForOptimizedProcessing = 64;

        /// <summary>
        /// Internal buffer for delay line
        /// </summary>
        protected float[] _delayLine;

        /// <summary>
        /// Current offset in delay line
        /// </summary>
        protected int _delayLineOffset;

        /// <summary>
        /// Constructor accepting the 32-bit kernel of a filter
        /// </summary>
        /// <param name="kernel"></param>
        public FirFilter(IEnumerable<float> kernel)
        {
            _kernel = kernel.ToArray();
            ResetInternals();
        }

        /// <summary>
        /// Constructor accepting the 64-bit kernel of a filter.
        /// 
        /// NOTE.
        /// This will simply cast values to floats!
        /// If you need to preserve precision for filter design & analysis, use constructor with TransferFunction!
        /// 
        /// </summary>
        /// <param name="kernel"></param>
        public FirFilter(IEnumerable<double> kernel) : this(kernel.ToFloats())
        {
        }

        /// <summary>
        /// Constructor accepting the transfer function.
        /// 
        /// Coefficients (used for filtering) will be cast to floats anyway,
        /// but filter will store the reference to TransferFunction object for FDA.
        /// 
        /// </summary>
        /// <param name="kernel"></param>
        public FirFilter(TransferFunction tf) : this(tf.Numerator.ToFloats())
        {
            Tf = tf;
        }

        /// <summary>
        /// Apply filter to entire signal (offline)
        /// </summary>
        /// <param name="signal"></param>
        /// <param name="method"></param>
        /// <returns></returns>
        public override DiscreteSignal ApplyTo(DiscreteSignal signal, 
                                               FilteringMethod method = FilteringMethod.Auto)
        {
            if (_kernel.Length >= FilterSizeForOptimizedProcessing && method == FilteringMethod.Auto)
            {
                method = FilteringMethod.OverlapAdd;
            }

            switch (method)
            {
                case FilteringMethod.OverlapAdd:
                {
                    var fftSize = MathUtils.NextPowerOfTwo(4 * _kernel.Length);
                    var blockConvolver = OlaBlockConvolver.FromFilter(this, fftSize);
                    return blockConvolver.ApplyTo(signal);
                }
                case FilteringMethod.OverlapSave:
                {
                    var fftSize = MathUtils.NextPowerOfTwo(4 * _kernel.Length);
                    var blockConvolver = OlsBlockConvolver.FromFilter(this, fftSize);
                    return blockConvolver.ApplyTo(signal);
                }
                case FilteringMethod.Custom:
                {
                    return this.ProcessChunks(signal);
                }
                default:
                {
                    return ApplyFilterDirectly(signal);
                }
            }
        }

        /// <summary>
        /// FIR online filtering (sample-by-sample)
        /// </summary>
        /// <param name="sample"></param>
        /// <returns></returns>
        public override float Process(float sample)
        {
            var output = 0.0f;

            _delayLine[_delayLineOffset] = sample;

            var pos = 0;
            for (var k = _delayLineOffset; k < _kernel.Length; k++)
            {
                output += _kernel[pos++] * _delayLine[k];
            }
            for (var k = 0; k < _delayLineOffset; k++)
            {
                output += _kernel[pos++] * _delayLine[k];
            }

            if (--_delayLineOffset < 0)
            {
                _delayLineOffset = _delayLine.Length - 1;
            }

            return output;
        }

        /// <summary>
        /// The most straightforward implementation of the difference equation:
        /// code the difference equation as it is
        /// </summary>
        /// <param name="signal"></param>
        /// <returns></returns>
        public DiscreteSignal ApplyFilterDirectly(DiscreteSignal signal)
        {
            var input = signal.Samples;

            var output = new float[input.Length + _kernel.Length - 1];

            for (var n = 0; n < output.Length; n++)
            {
                for (var k = 0; k < _kernel.Length; k++)
                {
                    if (n >= k && n < input.Length + k)
                    {
                        output[n] += _kernel[k] * input[n - k];
                    }
                }
            }

            return new DiscreteSignal(signal.SamplingRate, output);
        }

        /// <summary>
        /// Reset internal buffer
        /// </summary>
        private void ResetInternals()
        {
            if (_delayLine == null)
            {
                _delayLine = new float[_kernel.Length];
            }
            else
            {
                for (var i = 0; i < _delayLine.Length; i++)
                {
                    _delayLine[i] = 0;
                }
            }
            _delayLineOffset = _delayLine.Length - 1;
        }

        /// <summary>
        /// Reset filter
        /// </summary>
        public override void Reset() => ResetInternals();


        /// <summary>
        /// Sequential combination of two FIR filters (also an FIR filter)
        /// </summary>
        /// <param name="filter1"></param>
        /// <param name="filter2"></param>
        /// <returns></returns>
        public static FirFilter operator *(FirFilter filter1, FirFilter filter2)
        {
            var tf = filter1.Tf * filter2.Tf;

            return new FirFilter(tf.Numerator);
        }

        /// <summary>
        /// Sequential combination of an FIR and IIR filter
        /// </summary>
        /// <param name="filter1"></param>
        /// <param name="filter2"></param>
        /// <returns></returns>
        public static IirFilter operator *(FirFilter filter1, IirFilter filter2)
        {
            var tf = filter1.Tf * filter2.Tf;

            return new IirFilter(tf.Numerator, tf.Denominator);
        }

        /// <summary>
        /// Parallel combination of two FIR filters
        /// </summary>
        /// <param name="filter1"></param>
        /// <param name="filter2"></param>
        /// <returns></returns>
        public static FirFilter operator +(FirFilter filter1, FirFilter filter2)
        {
            var tf = filter1.Tf + filter2.Tf;

            return new FirFilter(tf.Numerator);
        }

        /// <summary>
        /// Parallel combination of an FIR and IIR filter
        /// </summary>
        /// <param name="filter1"></param>
        /// <param name="filter2"></param>
        /// <returns></returns>
        public static IirFilter operator +(FirFilter filter1, IirFilter filter2)
        {
            var tf = filter1.Tf + filter2.Tf;

            return new IirFilter(tf.Numerator, tf.Denominator);
        }
    }
}
