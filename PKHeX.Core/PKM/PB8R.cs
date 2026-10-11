using System;

namespace PKHeX.Core;

/// <summary>
/// BDSP-format Pokémon for Relumi saves. Extends <see cref="PB8"/> to use
/// <see cref="PersonalTable.Relumi"/> for personal data (all species up to gen 9).
/// </summary>
public sealed class PB8R : PB8
{
    public PB8R() { }
    public PB8R(Memory<byte> data) : base(data) { }
    public override PB8 Clone() => new PB8R(Data.ToArray());

    /// <summary> Converts to <see cref="PB8"/> by re-wrapping the same data. </summary>
    public PB8 ConvertToPB8() => new(Data.ToArray());

    public override PersonalInfo8BDSP PersonalInfo => PersonalTable.Relumi.GetFormEntry(Species, Form);

    // Unlock gen 9 move/species/item/ability ranges for the Relumi sandbox.
    public override ushort MaxMoveID => Legal.MaxMoveID_9;
    public override ushort MaxSpeciesID => Legal.MaxSpeciesID_9;
    public override int MaxAbilityID => Legal.MaxAbilityID_9;
    public override int MaxItemID => Legal.MaxItemID_9a;
    public override int MaxBallID => Legal.MaxBallID_9;
}