﻿using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using NWaves.Operations;
using NWaves.Signals;
using NWaves.Transforms;
using NWaves.Utils;

namespace NWaves.Filters.Base
{
    /// <summary>
    /// Class providing methods related to the transfer function of an LTI filter
    /// </summary>
    public class TransferFunction
    {
        /// <summary>
        /// Numerator of transfer function
        /// </summary>
        public double[] Numerator { get; private set; }

        /// <summary>
        /// Denominator of transfer function
        /// </summary>
        public double[] Denominator { get; private set; }

        /// <summary>
        /// Max iterations for calculating zeros/poles (roots of polynomials): 25000 by default
        /// </summary>
        public int CalculateZpIterations { get; set; } = MathUtils.PolyRootsIterations;

        /// <summary>
        /// TF constructor from numerator/denominator
        /// </summary>
        /// <param name="numerator"></param>
        /// <param name="denominator"></param>
        public TransferFunction(double[] numerator, double[] denominator = null)
        {
            Numerator = numerator;
            Denominator = denominator ?? new double[] { 1.0 };
        }

        /// <summary>
        /// TF constructor from zeros/poles
        /// </summary>
        /// <param name="zeros">Zeros</param>
        /// <param name="poles">Poles</param>
        /// <param name="gain"></param>
        public TransferFunction(ComplexDiscreteSignal zeros, ComplexDiscreteSignal poles, double gain = 1.0)
        {
            Gain = gain;
            Zeros = zeros;
            Poles = poles;
        }

        /// <summary>
        /// Zeros of TF
        /// </summary>
        private ComplexDiscreteSignal _zeros;
        public ComplexDiscreteSignal Zeros
        {
            get
            {
                return _zeros ?? TfToZp(Numerator, CalculateZpIterations);
            }
            private set
            {
                _zeros = value;
                Numerator = _zeros != null ? ZpToTf(_zeros) : new[] { 1.0 };

                for (var i = 0; i < Numerator.Length; i++)
                {
                    Numerator[i] *= Gain;
                }
            }
        }

        /// <summary>
        /// Poles of TF
        /// </summary>
        private ComplexDiscreteSignal _poles;
        public ComplexDiscreteSignal Poles
        {
            get
            {
                return _poles ?? TfToZp(Denominator, CalculateZpIterations);
            }
            private set
            {
                _poles = value;
                Denominator = _poles != null ? ZpToTf(_poles) : new[] { 1.0 };
            }
        }

        /// <summary>
        /// Gain ('k' in 'zpk' notation)
        /// </summary>
        public double Gain { get; private set; } = 1.0;


        /// <summary>
        /// Evaluate impulse response
        /// </summary>
        /// <param name="length"></param>
        /// <returns></returns>
        public double[] ImpulseResponse(int length = 512)
        {
            if (Denominator.Length == 1)
            {
                return Numerator.Length < length ? Numerator.PadZeros(length) : Numerator.FastCopy();
            }
            else
            {
                var b = Numerator;
                var a = Denominator;

                var response = new double[length];

                for (var n = 0; n < response.Length; n++)
                {
                    if (n < b.Length) response[n] = b[n];

                    for (var m = 1; m < a.Length; m++)
                    {
                        if (n >= m) response[n] -= a[m] * response[n - m];
                    }
                }

                return response;
            }
        }

        /// <summary>
        /// Evaluate frequency response
        /// </summary>
        /// <param name="length"></param>
        /// <returns></returns>
        public ComplexDiscreteSignal FrequencyResponse(int length = 512)
        {
            var real = ImpulseResponse(length);
            var imag = new double[length];

            var fft = new Fft64(length);
            fft.Direct(real, imag);

            return new ComplexDiscreteSignal(1, real.Take(length / 2 + 1),
                                                imag.Take(length / 2 + 1));
        }

        /// <summary>
        /// Group delay calculated from TF coefficients
        /// </summary>
        public double[] GroupDelay(int fftSize = 512)
        {
            var cc = Operation.CrossCorrelate(new ComplexDiscreteSignal(1, Numerator),
                                              new ComplexDiscreteSignal(1, Denominator)).Real;

            var cr = Enumerable.Range(0, cc.Length)
                               .Zip(cc, (r, c) => r * c)
                               .ToArray();

            var re = cc.PadZeros(fftSize);
            var im = new double[fftSize];
            
            var fft = new Fft64(fftSize);
            fft.Direct(re, im);

            var rre = cr.PadZeros(fftSize);
            var rim = new double[fftSize];
            fft.Direct(rre, rim);

            var num = rre.Zip(rim, (r, i) => new Complex(r, i)).ToArray();
            var den =  re.Zip( im, (r, i) => new Complex(r, i)).ToArray();

            var dn = Numerator.Length - 1;

            var gd = new double[fftSize / 2];
            for (var i = 1; i <= gd.Length; i++)
            {
                if (Complex.Abs(den[i]) < 1e-10)
                {
                    num[i] = Complex.Zero;
                    den[i] = Complex.One;
                }

                var t = dn - num[i] / den[i];
                gd[i - 1] = t.Real;
            }

            return gd;
        }

        /// <summary>
        /// Phase delay calculated from TF coefficients
        /// </summary>
        public double[] PhaseDelay(int fftSize = 512)
        {
            var gd = GroupDelay(fftSize);

            var pd = new double[gd.Length];
            var acc = 0.0;
            for (var i = 0; i < pd.Length; i++)     // integrate group delay
            {
                acc += gd[i];
                pd[i] = acc / (i + 1);
            }

            return pd;
        }

        /// <summary>
        /// Normalize frequency response at given frequency
        /// (normalize coefficients to map frequency response onto [0, 1])
        /// </summary>
        /// <param name="freq"></param>
        public void NormalizeAt(double freq)
        {
            var w = Complex.FromPolarCoordinates(1, freq);

            var gain = Complex.Abs(MathUtils.EvaluatePolynomial(Denominator, w) /
                                   MathUtils.EvaluatePolynomial(Numerator, w));

            for (var i = 0; i < Numerator.Length; i++)
            {
                Numerator[i] *= gain;
            }
        }

        /// <summary>
        /// Normalize numerator and denominator by Denominator[0]
        /// </summary>
        public void Normalize()
        {
            var a0 = Denominator[0];

            if (Math.Abs(a0) < 1e-10)
            {
                throw new ArgumentException("The first denominator coefficient can not be zero!");
            }

            for (var i = 0; i < Denominator.Length; i++)
            {
                Denominator[i] /= a0;
            }

            for (var i = 0; i < Numerator.Length; i++)
            {
                Numerator[i] /= a0;
            }
        }


        /// <summary>
        /// Method for converting zeros(poles) to TF numerator(denominator)
        /// </summary>
        /// <param name="zp"></param>
        /// <returns></returns>
        public static double[] ZpToTf(ComplexDiscreteSignal zp)
        {
            if (zp == null)
            {
                throw new ArgumentException("");
            }

            var tf = new ComplexDiscreteSignal(1, new[] { 1.0, -zp.Real[0] },
                                                  new[] { 0.0, -zp.Imag[0] });

            for (var k = 1; k < zp.Length; k++)
            {
                tf = Operation.Convolve(tf, new ComplexDiscreteSignal(1,
                                                  new[] { 1.0, -zp.Real[k] },
                                                  new[] { 0.0, -zp.Imag[k] }));
            }

            return tf.Real;
        }

        /// <summary>
        /// Method for converting zeros(poles) to TF numerator(denominator).
        /// Zeros and poles are given as double arrays of real and imaginary parts of zeros(poles).
        /// </summary>
        /// <param name="re"></param>
        /// <param name="im"></param>
        /// <returns></returns>
        public static double[] ZpToTf(double[] re, double[] im = null) => ZpToTf(new ComplexDiscreteSignal(1, re, im));

        /// <summary>
        /// Method for converting TF numerator(denominator) to zeros(poles)
        /// </summary>
        /// <param name="tf"></param>
        /// <returns></returns>
        public static ComplexDiscreteSignal TfToZp(double[] tf, int maxIterations = MathUtils.PolyRootsIterations)
        {
            if (tf.Length <= 1)
            {
                return null;
            }

            var roots = MathUtils.PolynomialRoots(tf, maxIterations);

            return new ComplexDiscreteSignal(1, roots.Select(r => r.Real),
                                                roots.Select(r => r.Imaginary));
        }

        /// <summary>
        /// Sequential connection
        /// </summary>
        /// <param name="tf1"></param>
        /// <param name="tf2"></param>
        /// <returns></returns>
        public static TransferFunction operator *(TransferFunction tf1, TransferFunction tf2)
        {
            var num = Operation.Convolve(tf1.Numerator, tf2.Numerator);
            var den = Operation.Convolve(tf1.Denominator, tf2.Denominator);

            return new TransferFunction(num, den);
        }

        /// <summary>
        /// Parallel connection
        /// </summary>
        /// <param name="tf1"></param>
        /// <param name="tf2"></param>
        /// <returns></returns>
        public static TransferFunction operator +(TransferFunction tf1, TransferFunction tf2)
        {
            var num1 = Operation.Convolve(tf1.Numerator, tf2.Denominator);
            var num2 = Operation.Convolve(tf2.Numerator, tf1.Denominator);

            var num = num1;
            var add = num2;

            if (num1.Length < num2.Length)
            {
                num = num2;
                add = num1;
            }

            for (var i = 0; i < add.Length; i++)
            {
                num[i] += add[i];
            }

            var den = Operation.Convolve(tf1.Denominator, tf2.Denominator);

            return new TransferFunction(num, den);
        }

        /// <summary>
        /// Load TF numerator and denominator from csv file
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="delimiter"></param>
        public static TransferFunction FromCsv(Stream stream, char delimiter = ',')
        {
            using (var reader = new StreamReader(stream))
            {
                var content = reader.ReadLine();
                var numerator = content.Split(delimiter)
                                       .Select(s => double.Parse(s, NumberStyles.Any, CultureInfo.InvariantCulture))
                                       .ToArray();

                content = reader.ReadLine();
                var denominator = content.Split(delimiter)
                                         .Select(s => double.Parse(s, NumberStyles.Any, CultureInfo.InvariantCulture))
                                         .ToArray();

                return new TransferFunction(numerator, denominator);
            }
        }

        /// <summary>
        /// Serialize TF numerator and denominator to csv file
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="delimiter"></param>
        public void ToCsv(Stream stream, char delimiter = ',')
        {
            using (var writer = new StreamWriter(stream))
            {
                var content = string.Join(delimiter.ToString(), Numerator.Select(k => k.ToString(CultureInfo.InvariantCulture)));
                writer.WriteLine(content);

                content = string.Join(delimiter.ToString(), Denominator.Select(k => k.ToString(CultureInfo.InvariantCulture)));
                writer.WriteLine(content);
            }
        }
    }
}
