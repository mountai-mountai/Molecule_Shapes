// Minimal stub so PairGroup compiles. Generic ("Model" screen) pair groups pass null.
// Expand later (covalent radius, CPK color, atomic number) for the Real Molecules screen.

namespace Molecule_Shapes.Model
{
    public class Element
    {
        public readonly string Symbol;

        public Element(string symbol)
        {
            Symbol = symbol;
        }
    }
}
