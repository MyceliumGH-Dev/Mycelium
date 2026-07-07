using System;

namespace Mycelium.Core
{
    /// <summary>
    /// OpenSimplex noise generator (K.S. Peterson, 2014).
    /// Patent-free noise evaluated on a simplicial (triangular) grid for mathematical isotropy.
    /// Unlike Perlin noise, which evaluates on a Cartesian grid and exhibits directional
    /// artifacts, OpenSimplex distributes gradients on a triangular lattice, so the noise
    /// field has no preferred direction.
    /// </summary>
    public class OpenSimplexNoise
    {
        // Skew and unskew constants for the 2D simplex grid:
        // STRETCH maps the square grid to the simplicial grid, SQUISH maps it back.
        private const double STRETCH_2D = -0.211324865405187;  // (1/sqrt(2+1)-1)/2
        private const double SQUISH_2D = 0.366025403784439;    // (sqrt(2+1)-1)/2
        private const double NORM_2D = 47.0;

        private readonly short[] perm;
        private readonly short[] permGradIndex2D;

        // 8 gradient directions evenly spaced on the unit circle
        private static readonly sbyte[] gradients2D = {
             5,  2,    2,  5,   -5,  2,   -2,  5,
             5, -2,    2, -5,   -5, -2,   -2, -5,
        };

        public OpenSimplexNoise(int seed)
        {
            perm = new short[256];
            permGradIndex2D = new short[256];
            short[] source = new short[256];
            for (short i = 0; i < 256; i++)
                source[i] = i;

            // LCG-based seeding (standard OpenSimplex approach)
            long s = (long)seed;
            s = s * 6364136223846793005L + 1442695040888963407L;
            s = s * 6364136223846793005L + 1442695040888963407L;
            s = s * 6364136223846793005L + 1442695040888963407L;

            for (int i = 255; i >= 0; i--)
            {
                s = s * 6364136223846793005L + 1442695040888963407L;
                int r = (int)((s + 31) % (i + 1));
                if (r < 0) r += (i + 1);
                perm[i] = source[r];
                permGradIndex2D[i] = (short)((perm[i] % (gradients2D.Length / 2)) * 2);
                source[r] = source[i];
            }
        }

        /// <summary>
        /// Evaluate 2D OpenSimplex noise at coordinates (x, y).
        /// Returns a value in approximately [-1, 1].
        /// </summary>
        public double Evaluate(double x, double y)
        {
            // Stretch input space to determine which simplex cell we're in
            double stretchOffset = (x + y) * STRETCH_2D;
            double xs = x + stretchOffset;
            double ys = y + stretchOffset;

            // Floor to get base simplex cell coordinates
            int xsb = FastFloor(xs);
            int ysb = FastFloor(ys);

            // Squish back to get position relative to cell origin in real space
            double squishOffset = (xsb + ysb) * SQUISH_2D;
            double xb = xsb + squishOffset;
            double yb = ysb + squishOffset;

            // Fractional position within the stretched cell
            double xins = xs - xsb;
            double yins = ys - ysb;

            // Sum to determine which simplex triangle we're in
            double inSum = xins + yins;

            // Position relative to the cell origin in real space
            double dx0 = x - xb;
            double dy0 = y - yb;

            double value = 0;

            // Contribution from (0, 0)
            double attn0 = 2.0 - dx0 * dx0 - dy0 * dy0;
            if (attn0 > 0)
            {
                attn0 *= attn0;
                value += attn0 * attn0 * Extrapolate(xsb, ysb, dx0, dy0);
            }

            // Contribution from (1, 0)
            double dx1 = dx0 - 1.0 - SQUISH_2D;
            double dy1 = dy0 - SQUISH_2D;
            double attn1 = 2.0 - dx1 * dx1 - dy1 * dy1;
            if (attn1 > 0)
            {
                attn1 *= attn1;
                value += attn1 * attn1 * Extrapolate(xsb + 1, ysb, dx1, dy1);
            }

            // Contribution from (0, 1)
            double dx2 = dx0 - SQUISH_2D;
            double dy2 = dy0 - 1.0 - SQUISH_2D;
            double attn2 = 2.0 - dx2 * dx2 - dy2 * dy2;
            if (attn2 > 0)
            {
                attn2 *= attn2;
                value += attn2 * attn2 * Extrapolate(xsb, ysb + 1, dx2, dy2);
            }

            // Determine extra vertex based on which simplex triangle we're in
            double dx_ext, dy_ext;
            int xsv_ext, ysv_ext;

            if (inSum <= 1.0)
            {
                // Triangle (0,0)-(1,0)-(0,1): closer to origin
                double zins = 1.0 - inSum;
                if (zins > xins || zins > yins)
                {
                    if (xins > yins)
                    {
                        xsv_ext = xsb + 1;
                        ysv_ext = ysb - 1;
                        dx_ext = dx0 - 1.0;
                        dy_ext = dy0 + 1.0;
                    }
                    else
                    {
                        xsv_ext = xsb - 1;
                        ysv_ext = ysb + 1;
                        dx_ext = dx0 + 1.0;
                        dy_ext = dy0 - 1.0;
                    }
                }
                else
                {
                    xsv_ext = xsb + 1;
                    ysv_ext = ysb + 1;
                    dx_ext = dx0 - 1.0 - 2.0 * SQUISH_2D;
                    dy_ext = dy0 - 1.0 - 2.0 * SQUISH_2D;
                }
            }
            else
            {
                // Triangle (1,0)-(0,1)-(1,1): closer to (1,1)
                double zins = 2.0 - inSum;
                if (zins < xins || zins < yins)
                {
                    if (xins > yins)
                    {
                        xsv_ext = xsb + 2;
                        ysv_ext = ysb;
                        dx_ext = dx0 - 2.0 - 2.0 * SQUISH_2D;
                        dy_ext = dy0 - 2.0 * SQUISH_2D;
                    }
                    else
                    {
                        xsv_ext = xsb;
                        ysv_ext = ysb + 2;
                        dx_ext = dx0 - 2.0 * SQUISH_2D;
                        dy_ext = dy0 - 2.0 - 2.0 * SQUISH_2D;
                    }
                }
                else
                {
                    xsv_ext = xsb;
                    ysv_ext = ysb;
                    dx_ext = dx0;
                    dy_ext = dy0;
                }
                xsb += 1;
                ysb += 1;
                dx0 = dx0 - 1.0 - 2.0 * SQUISH_2D;
                dy0 = dy0 - 1.0 - 2.0 * SQUISH_2D;
            }

            // Contribution from extra vertex
            double attn_ext = 2.0 - dx_ext * dx_ext - dy_ext * dy_ext;
            if (attn_ext > 0)
            {
                attn_ext *= attn_ext;
                value += attn_ext * attn_ext * Extrapolate(xsv_ext, ysv_ext, dx_ext, dy_ext);
            }

            return value / NORM_2D;
        }

        private double Extrapolate(int xsb, int ysb, double dx, double dy)
        {
            int index = permGradIndex2D[(perm[xsb & 0xFF] + ysb) & 0xFF];
            return gradients2D[index] * dx + gradients2D[index + 1] * dy;
        }

        private static int FastFloor(double x)
        {
            int xi = (int)x;
            return x < xi ? xi - 1 : xi;
        }
    }
}
