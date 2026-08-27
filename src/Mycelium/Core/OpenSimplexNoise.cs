using System;

namespace Mycelium.Core
{
    /// <summary>
    /// 2D continuous simplex noise generator.
    /// </summary>
    public class OpenSimplexNoise
    {
        private const long PrimeX = 0x5205402B9270C86FL;
        private const long PrimeY = 0x598CD327003817B5L;
        private const long HashMultiplier = 0x53A3F72DEEC546F5L;

        private const double Skew2D = 0.366025403784439;      // (sqrt(2+1)-1)/2
        private const double Unskew2D = -0.21132486540518713;  // (1/sqrt(2+1)-1)/2
        private const double RSquared2D = 2.0 / 3.0;
        private const double Normalizer2D = 18.24195727440366; // Scales output to [-1, 1]

        private const int NGrads2DExponent = 7;
        private const int NGrads2D = 1 << NGrads2DExponent;
        private static readonly double[] Gradients2D;

        private readonly long _seed;

        static OpenSimplexNoise()
        {
            Gradients2D = new double[NGrads2D * 2];
            for (int i = 0; i < NGrads2D; i++)
            {
                double angle = (i * 2.0 * Math.PI) / NGrads2D;
                Gradients2D[i * 2] = Math.Cos(angle);
                Gradients2D[i * 2 + 1] = Math.Sin(angle);
            }
        }

        public OpenSimplexNoise(int seed)
        {
            _seed = seed;
        }

        public OpenSimplexNoise(long seed)
        {
            _seed = seed;
        }

        /// <summary>
        /// Evaluates 2D SuperSimplex noise at (x, y). Returns a smooth value in approximately [-1, 1].
        /// </summary>
        public double Evaluate(double x, double y)
        {
            // Skew transform to get coordinates in stretched simplex lattice space
            double s = Skew2D * (x + y);
            double xs = x + s;
            double ys = y + s;

            // Base simplex cell coordinates
            int xsb = FastFloor(xs);
            int ysb = FastFloor(ys);
            double xi = xs - xsb;
            double yi = ys - ysb;

            // Prime pre-multiplication for hash
            long xsbp = xsb * PrimeX;
            long ysbp = ysb * PrimeY;

            // Unskew back to real space offsets from base vertex (0, 0)
            double t = (xi + yi) * Unskew2D;
            double dx0 = xi + t;
            double dy0 = yi + t;

            double value = 0.0;

            // Vertex (0, 0)
            double a0 = RSquared2D - dx0 * dx0 - dy0 * dy0;
            if (a0 > 0)
            {
                double a0Sq = a0 * a0;
                value += a0Sq * a0Sq * Grad(_seed, xsbp, ysbp, dx0, dy0);
            }

            // Vertex (1, 1)
            double dx1 = dx0 - (1.0 + 2.0 * Unskew2D);
            double dy1 = dy0 - (1.0 + 2.0 * Unskew2D);
            double a1 = RSquared2D - dx1 * dx1 - dy1 * dy1;
            if (a1 > 0)
            {
                double a1Sq = a1 * a1;
                value += a1Sq * a1Sq * Grad(_seed, xsbp + PrimeX, ysbp + PrimeY, dx1, dy1);
            }

            // Third and fourth vertices based on which simplex region we are in
            double xmyi = xi - yi;
            if (t < Unskew2D)
            {
                if (xi + xmyi > 1.0)
                {
                    double dx2 = dx0 - (3.0 * Unskew2D + 2.0);
                    double dy2 = dy0 - (3.0 * Unskew2D + 1.0);
                    double a2 = RSquared2D - dx2 * dx2 - dy2 * dy2;
                    if (a2 > 0)
                    {
                        double a2Sq = a2 * a2;
                        value += a2Sq * a2Sq * Grad(_seed, xsbp + (PrimeX << 1), ysbp + PrimeY, dx2, dy2);
                    }
                }
                else
                {
                    double dx2 = dx0 - Unskew2D;
                    double dy2 = dy0 - (Unskew2D + 1.0);
                    double a2 = RSquared2D - dx2 * dx2 - dy2 * dy2;
                    if (a2 > 0)
                    {
                        double a2Sq = a2 * a2;
                        value += a2Sq * a2Sq * Grad(_seed, xsbp, ysbp + PrimeY, dx2, dy2);
                    }
                }

                if (yi - xmyi > 1.0)
                {
                    double dx3 = dx0 - (3.0 * Unskew2D + 1.0);
                    double dy3 = dy0 - (3.0 * Unskew2D + 2.0);
                    double a3 = RSquared2D - dx3 * dx3 - dy3 * dy3;
                    if (a3 > 0)
                    {
                        double a3Sq = a3 * a3;
                        value += a3Sq * a3Sq * Grad(_seed, xsbp + PrimeX, ysbp + (PrimeY << 1), dx3, dy3);
                    }
                }
                else
                {
                    double dx3 = dx0 - (Unskew2D + 1.0);
                    double dy3 = dy0 - Unskew2D;
                    double a3 = RSquared2D - dx3 * dx3 - dy3 * dy3;
                    if (a3 > 0)
                    {
                        double a3Sq = a3 * a3;
                        value += a3Sq * a3Sq * Grad(_seed, xsbp + PrimeX, ysbp, dx3, dy3);
                    }
                }
            }
            else
            {
                if (xi + xmyi < 0.0)
                {
                    double dx2 = dx0 + (1.0 + Unskew2D);
                    double dy2 = dy0 + Unskew2D;
                    double a2 = RSquared2D - dx2 * dx2 - dy2 * dy2;
                    if (a2 > 0)
                    {
                        double a2Sq = a2 * a2;
                        value += a2Sq * a2Sq * Grad(_seed, xsbp - PrimeX, ysbp, dx2, dy2);
                    }
                }
                else
                {
                    double dx2 = dx0 - (Unskew2D + 1.0);
                    double dy2 = dy0 - Unskew2D;
                    double a2 = RSquared2D - dx2 * dx2 - dy2 * dy2;
                    if (a2 > 0)
                    {
                        double a2Sq = a2 * a2;
                        value += a2Sq * a2Sq * Grad(_seed, xsbp + PrimeX, ysbp, dx2, dy2);
                    }
                }

                if (yi < xmyi)
                {
                    double dx3 = dx0 + Unskew2D;
                    double dy3 = dy0 + (1.0 + Unskew2D);
                    double a3 = RSquared2D - dx3 * dx3 - dy3 * dy3;
                    if (a3 > 0)
                    {
                        double a3Sq = a3 * a3;
                        value += a3Sq * a3Sq * Grad(_seed, xsbp, ysbp - PrimeY, dx3, dy3);
                    }
                }
                else
                {
                    double dx3 = dx0 - Unskew2D;
                    double dy3 = dy0 - (Unskew2D + 1.0);
                    double a3 = RSquared2D - dx3 * dx3 - dy3 * dy3;
                    if (a3 > 0)
                    {
                        double a3Sq = a3 * a3;
                        value += a3Sq * a3Sq * Grad(_seed, xsbp, ysbp + PrimeY, dx3, dy3);
                    }
                }
            }

            return value * Normalizer2D;
        }

        private static double Grad(long seed, long xsbp, long ysbp, double dx, double dy)
        {
            long hash = seed ^ xsbp ^ ysbp;
            hash *= HashMultiplier;
            hash ^= (long)((ulong)hash >> (64 - NGrads2DExponent + 1));
            int gi = (int)hash & ((NGrads2D - 1) << 1);
            return Gradients2D[gi] * dx + Gradients2D[gi + 1] * dy;
        }

        private static int FastFloor(double x)
        {
            int xi = (int)x;
            return x < xi ? xi - 1 : xi;
        }
    }
}
