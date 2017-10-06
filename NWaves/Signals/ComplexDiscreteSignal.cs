﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace NWaves.Signals
{
    /// <summary>
    /// Base class for finite complex-valued discrete-time signals.
    /// 
    /// Any finite complex DT signal is stored as two arrays of data (real parts and imaginary parts)
    /// sampled at certain sampling rate.
    /// 
    /// See also ComplexDiscreteSignalExtensions for additional functionality of complex DT signals.
    /// 
    /// Note.
    /// Method implementations are LINQ-less for better performance.
    /// </summary>
    public class ComplexDiscreteSignal
    {
        /// <summary>
        /// Number of samples per unit of time (1 second)
        /// </summary>
        public virtual int SamplingRate { get; }

        /// <summary>
        /// Array or real parts of samples
        /// </summary>
        public virtual double[] Real { get; }

        /// <summary>
        /// Array or imaginary parts of samples
        /// </summary>
        public virtual double[] Imag { get; }

        /// <summary>
        /// The most efficient constructor for initializing complex signals
        /// </summary>
        /// <param name="samplingRate"></param>
        /// <param name="real"></param>
        /// <param name="imag"></param>
        public ComplexDiscreteSignal(int samplingRate, double[] real, double[] imag = null)
        {
            if (samplingRate <= 0)
            {
                throw new ArgumentException("Sampling rate must be positive!");
            }

            SamplingRate = samplingRate;

            var realSamples = new double[real.Length];
            var imagSamples = new double[real.Length];

            Buffer.BlockCopy(real, 0, realSamples, 0, real.Length * 8);

            if (imag != null)
            {
                if (imag.Length != real.Length)
                {
                    throw new ArgumentException("Arrays of real and imaginary parts have different size!");
                }

                Buffer.BlockCopy(imag, 0, imagSamples, 0, imag.Length * 8);
            }

            Real = realSamples;
            Imag = imagSamples;
        }

        /// <summary>
        /// Constructor for initializing complex signals with any double enumerables
        /// </summary>
        /// <param name="samplingRate"></param>
        /// <param name="real"></param>
        /// <param name="imag"></param>
        public ComplexDiscreteSignal(int samplingRate, IEnumerable<double> real, IEnumerable<double> imag = null)
            : this(samplingRate, real.ToArray(), imag?.ToArray())
        {
        }

        /// <summary>
        /// Constructor creates the complex signal of specified length filled with specified values
        /// </summary>
        /// <param name="samplingRate"></param>
        /// <param name="length"></param>
        /// <param name="real"></param>
        /// <param name="imag"></param>
        public ComplexDiscreteSignal(int samplingRate, int length, double real = 0.0, double imag = 0.0)
        {
            if (samplingRate <= 0)
            {
                throw new ArgumentException("Sampling rate must be positive!");
            }

            SamplingRate = samplingRate;

            var reals = new double[length];
            var imags = new double[length];
            for (var i = 0; i < length; i++)
            {
                reals[i] = real;
                imags[i] = imag;
            }
            Real = reals;
            Imag = imags;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="samplingRate"></param>
        /// <param name="samples"></param>
        /// <param name="normalizeFactor"></param>
        public ComplexDiscreteSignal(int samplingRate, IEnumerable<int> samples, double normalizeFactor = 1.0)
        {
            if (samplingRate <= 0)
            {
                throw new ArgumentException("Sampling rate must be positive!");
            }

            SamplingRate = samplingRate;

            var intSamples = samples.ToArray();
            var realSamples = new double[intSamples.Length];
            
            for (var i = 0; i < intSamples.Length; i++)
            {
                realSamples[i] = intSamples[i] / normalizeFactor;
            }

            Real = realSamples;
            Imag = new double[intSamples.Length];
        }

        /// <summary>
        /// Create a copy of complex signal
        /// </summary>
        /// <returns>New copied signal</returns>
        public ComplexDiscreteSignal Copy()
        {
            return new ComplexDiscreteSignal(SamplingRate, Real, Imag);
        }

        /// <summary>
        /// Indexer works only with array of real parts of samples. Use it with caution.
        /// </summary>
        public virtual double this[int index]
        { 
            get { return Real[index]; }
            set { Real[index] = value; }
        }

        /// <summary>
        /// Slice the signal (Python-style)
        /// 
        ///     var middle = signal[900, 1200];
        /// 
        /// Implementaion is LINQ-less, since Skip() would be less efficient:
        ///                 
        ///     return new DiscreteSignal(SamplingRate, 
        ///                               Real.Skip(startPos).Take(endPos - startPos),
        ///                               Imag.Skip(startPos).Take(endPos - startPos));
        /// </summary>
        /// <param name="startPos">Position of the first sample</param>
        /// <param name="endPos">Position of the last sample (exclusive)</param>
        /// <returns>Slice of the signal</returns>
        /// <exception>Overflow possible if endPos is less than startPos</exception>
        public virtual ComplexDiscreteSignal this[int startPos, int endPos]
        {
            get
            {
                var rangeLength = endPos - startPos;

                if (rangeLength <= 0)
                {
                    throw new ArgumentException("Wrong index range!");
                }

                var realSamples = new double[rangeLength];
                Buffer.BlockCopy(Real, startPos * 8, realSamples, 0, rangeLength * 8);

                var imagSamples = new double[rangeLength];
                Buffer.BlockCopy(Imag, startPos * 8, imagSamples, 0, rangeLength * 8);

                return new ComplexDiscreteSignal(SamplingRate, realSamples, imagSamples);
            }
        }

        /// <summary>
        /// Overloaded operator+ for signals concatenates these signals
        /// </summary>
        /// <param name="s1">First complex signal</param>
        /// <param name="s2">Second complex signal</param>
        /// <returns></returns>
        public static ComplexDiscreteSignal operator +(ComplexDiscreteSignal s1, ComplexDiscreteSignal s2)
        {
            return s1.Concatenate(s2);
        }

        /// <summary>
        /// Overloaded operator+ for some number performs signal delay by this number
        /// </summary>
        /// <param name="s">Complex signal</param>
        /// <param name="delay">Number of samples</param>
        /// <returns></returns>
        public static ComplexDiscreteSignal operator +(ComplexDiscreteSignal s, int delay)
        {
            return s.Delay(delay);
        }

        /// <summary>
        /// Overloaded operator* repeats signal several times
        /// </summary>
        /// <param name="s">Complex signal</param>
        /// <param name="times">Number of times</param>
        /// <returns></returns>
        public static ComplexDiscreteSignal operator *(ComplexDiscreteSignal s, int times)
        {
            return s.Repeat(times);
        }
    }
}
