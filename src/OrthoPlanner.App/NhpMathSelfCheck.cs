#if DEBUG
using System;
using System.Windows.Media.Media3D;

namespace OrthoPlanner.App;

/// <summary>
/// One-time DEBUG self-check of the NHP matrix primitives (spec §7 — no test framework).
/// Asserts identity-at-zero, an invertible round-trip, and rigidity (rotation 3×3 det = 1, no scale) —
/// the property INV7/INV9 rely on for radius/length preservation. Both the identity and round-trip are
/// center/six-independent, so the check needs no loaded volume; runs once at startup and on failure pops
/// the default <see cref="System.Diagnostics.Debug.Assert"/> dialog.
/// </summary>
internal static class NhpMathSelfCheck
{
    public static void Run()
    {
        const double tol = 1e-9;

        // 1. Identity at zero (for any center — translation/rotation by 0 collapse to identity).
        var zero = ViewModels.MainViewModel.BuildNhpMatrix(default, 0, 0, 0, 0, 0, 0);
        System.Diagnostics.Debug.Assert(zero.IsIdentity, "NHP: BuildNhpMatrix(0..0) must be Identity");

        // 2. Invertible round-trip: matrix * inverse == Identity (six/center-independent).
        var m = ViewModels.MainViewModel.BuildNhpMatrix(default, 10, 20, 30, 5, 8, 12);
        var inv = m; inv.Invert();
        var composed = m * inv;
        System.Diagnostics.Debug.Assert(
            Math.Abs(composed.M11 - 1) < tol && Math.Abs(composed.M22 - 1) < tol && Math.Abs(composed.M33 - 1) < tol &&
            Math.Abs(composed.OffsetX) < tol && Math.Abs(composed.OffsetY) < tol && Math.Abs(composed.OffsetZ) < tol,
            "NHP: matrix * inverse must be Identity");

        // 3. Rigid: the upper-left 3x3 is a pure rotation (det = 1) — no scale/shear. This is what preserves
        //    ceph-sphere radius (INV4) and splint wafer seating (INV9) through NhpShared; a regression that
        //    introduced scale would break both.
        double det = m.M11 * (m.M22 * m.M33 - m.M23 * m.M32)
                   - m.M12 * (m.M21 * m.M33 - m.M23 * m.M31)
                   + m.M13 * (m.M21 * m.M32 - m.M22 * m.M31);
        System.Diagnostics.Debug.Assert(Math.Abs(det - 1) < tol, "NHP: matrix must be rigid (rotation 3x3 det=1, no scale)");
    }
}
#endif
