// Port of MoleculeGeometry.js - the "molecular geometry": the shape described by the ATOMS only.
// Lone pairs influence which geometry results, but are not part of the named shape the user sees.
// Mapping from (x = radial atoms, e = radial lone pairs) -> shape name is taken verbatim from PhET.

namespace Molecule_Shapes.Model
{
    public enum MoleculeGeometryKind
    {
        Empty,
        Diatomic,
        Linear,
        Bent,
        TrigonalPlanar,
        TrigonalPyramidal,
        TShaped,
        Tetrahedral,
        Seesaw,
        SquarePlanar,
        TrigonalBipyramidal,
        SquarePyramidal,
        Octahedral
    }

    public class MoleculeGeometry
    {
        public readonly MoleculeGeometryKind Kind;
        public readonly int X;            // number of radial atoms
        public readonly string DisplayName;

        private MoleculeGeometry(MoleculeGeometryKind kind, int x, string displayName)
        {
            Kind = kind;
            X = x;
            DisplayName = displayName;
        }

        public static readonly MoleculeGeometry Empty               = new(MoleculeGeometryKind.Empty, 0, "");
        public static readonly MoleculeGeometry Diatomic            = new(MoleculeGeometryKind.Diatomic, 1, "Diatomic");
        public static readonly MoleculeGeometry Linear              = new(MoleculeGeometryKind.Linear, 2, "Linear");               // e = 0,3,4
        public static readonly MoleculeGeometry Bent                = new(MoleculeGeometryKind.Bent, 2, "Bent");                   // e = 1,2
        public static readonly MoleculeGeometry TrigonalPlanar      = new(MoleculeGeometryKind.TrigonalPlanar, 3, "Trigonal Planar");       // e = 0
        public static readonly MoleculeGeometry TrigonalPyramidal   = new(MoleculeGeometryKind.TrigonalPyramidal, 3, "Trigonal Pyramidal"); // e = 1
        public static readonly MoleculeGeometry TShaped             = new(MoleculeGeometryKind.TShaped, 3, "T-shaped");            // e = 2,3
        public static readonly MoleculeGeometry Tetrahedral         = new(MoleculeGeometryKind.Tetrahedral, 4, "Tetrahedral");     // e = 0
        public static readonly MoleculeGeometry Seesaw              = new(MoleculeGeometryKind.Seesaw, 4, "Seesaw");               // e = 1
        public static readonly MoleculeGeometry SquarePlanar        = new(MoleculeGeometryKind.SquarePlanar, 4, "Square Planar");  // e = 2
        public static readonly MoleculeGeometry TrigonalBipyramidal = new(MoleculeGeometryKind.TrigonalBipyramidal, 5, "Trigonal Bipyramidal"); // e = 0
        public static readonly MoleculeGeometry SquarePyramidal     = new(MoleculeGeometryKind.SquarePyramidal, 5, "Square Pyramidal");         // e = 1
        public static readonly MoleculeGeometry Octahedral          = new(MoleculeGeometryKind.Octahedral, 6, "Octahedral");       // e = 0

        // x = number of radial atoms, e = number of radial lone pairs.
        public static MoleculeGeometry GetConfiguration(int x, int e)
        {
            switch (x)
            {
                case 0:
                    return Empty;
                case 1:
                    return Diatomic;
                case 2:
                    if (e == 0 || e == 3 || e == 4) return Linear;
                    if (e == 1 || e == 2) return Bent;
                    break;
                case 3:
                    if (e == 0) return TrigonalPlanar;
                    if (e == 1) return TrigonalPyramidal;
                    if (e == 2 || e == 3) return TShaped;
                    break;
                case 4:
                    if (e == 0) return Tetrahedral;
                    if (e == 1) return Seesaw;
                    if (e == 2) return SquarePlanar;
                    break;
                case 5:
                    if (e == 0) return TrigonalBipyramidal;
                    if (e == 1) return SquarePyramidal;
                    break;
                case 6:
                    if (e == 0) return Octahedral;
                    break;
            }
            throw new System.ArgumentException($"invalid VSEPR configuration x: {x}, e: {e}");
        }
    }
}
