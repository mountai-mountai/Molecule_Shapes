// Vocabulary for the gamified layer. All pure C# (no Unity dependency) so it stays EditMode-testable
// alongside the model. These enums are the "selectable" axes the player chooses from:
//   - GameMode:           the overall activity (sandbox vs a scored game).
//   - LearningObjective:  WHAT the player is practicing (the criteria you pick by what you want to learn).
//   - TaskMode/MatchMode: the mechanical consequences of an objective (do you build it, or name it?).
//   - ChallengeDifficulty: which configurations are drawn, and the default scoring weighting.
//
// Note: BuildMolecularGeometry / BuildElectronGeometry and their Identify* counterparts are the
// TEKS-facing objectives. BuildFromAxe/IdentifyAxe/IdentifyBoth are AXE-notation "advanced" modes
// (hidden behind a setting); BuildRealMolecule is scaffolded and hidden until its real-molecule table.

namespace Molecule_Shapes.Game
{
    // The overall activity the session is running.
    public enum GameMode
    {
        Sandbox,        // free play - today's behavior, no scoring (the "off" state)
        Challenge       // scored challenges driven by a LearningObjective
    }

    // What the player is practicing. This is the criteria selector: pick by what you want to learn.
    // NOTE: order is fixed - Unity serializes these by their integer index, so rename in place and only
    // ever APPEND new values (BuildElectronGeometry / IdentifyElectronGeometry were appended).
    public enum LearningObjective
    {
        BuildMolecularGeometry,     // shown a molecular-geometry name ("Bent"), build any molecule with that shape
        BuildFromAxe,               // shown an AXE formula ("AX2E2"), build exactly that count [advanced/AXE]
        BuildFromAngles,            // shown target bond angles, build a molecule that settles to them
        IdentifyMolecularGeometry,  // shown a molecule, choose its molecular-geometry name
        IdentifyAxe,                // shown a molecule, choose its AXE formula [advanced/AXE]
        IdentifyBoth,               // shown a molecule, choose "Name (AXE)" [advanced/AXE]
        BuildRealMolecule,          // shown a real molecule's formula, build it [needs the real-molecule table]
        BuildElectronGeometry,      // shown an electron-geometry name, build any molecule with that steric number
        IdentifyElectronGeometry    // shown a molecule, choose its electron-geometry name
    }

    // The mechanical kind of task an objective resolves to.
    public enum TaskMode
    {
        Build,      // the answer is the state of the molecule the player manipulates
        Identify    // the answer is a multiple-choice selection; the molecule is shown, not edited
    }

    // How a Build challenge decides "correct".
    public enum MatchMode
    {
        ExactCounts,        // radial atoms (X) AND radial lone pairs (E) must both match the goal
        GeometryName,       // the resulting molecular-geometry name must match (several X/E can satisfy it,
                            // e.g. Linear = AX2 or AX2E3) - the right granularity when the prompt is a name
        ElectronGeometry    // the electron geometry must match, i.e. the same steric number (X+E) - any
                            // arrangement of bonds/lone pairs summing to it counts
    }

    // Difficulty tier - selects which goal configurations are eligible and the default ScoreRules preset.
    public enum ChallengeDifficulty
    {
        Easy,       // no lone pairs, low steric number (Linear, Trigonal Planar, Tetrahedral)
        Medium,     // higher steric number or a single lone pair (Trig. Bipyramidal, Octahedral, Seesaw...)
        Hard,       // lone-pair-rich shapes (Bent, T-shaped, Square Planar, Square Pyramidal)
        Mixed       // draws from all tiers
    }
}
