using System;

namespace PKHeX.Core;

/// <summary>
/// Personal Table for Relumi save format. Uses BDSP-format entries (<see cref="PersonalInfo8BDSP"/>)
/// covering all species and forms present in the Relumi game.
/// The binary is generated from <c>GameData/PersonalTable.json</c> by the
/// <c>GameData/generate.csproj</c> tool — re-run it whenever the JSON is updated.
/// </summary>
public sealed class PersonalTableRelumi : IPersonalTable, IPersonalTable<PersonalInfo8BDSP>
{
    private readonly PersonalInfo8BDSP[] Table;
    private const int SIZE = PersonalInfo8BDSP.SIZE; // 0x44
    private const ushort MaxSpecies = Legal.MaxSpeciesID_9;
    public ushort MaxSpeciesID => MaxSpecies;
    public int Count => Table.Length;

    public PersonalTableRelumi()
    {
        var data = Util.GetBinaryResource("personal_relumi");
        int count = data.Length / SIZE;
        Table = new PersonalInfo8BDSP[count];
        var mem = data.AsMemory();
        for (int i = 0; i < count; i++)
            Table[i] = new PersonalInfo8BDSP(mem.Slice(i * SIZE, SIZE));
    }

    public PersonalInfo8BDSP this[int index] => Table[(uint)index < Table.Length ? index : 0];
    public PersonalInfo8BDSP this[ushort species, byte form] => Table[GetFormIndex(species, form)];
    public PersonalInfo8BDSP GetFormEntry(ushort species, byte form) => Table[GetFormIndex(species, form)];

    public int GetFormIndex(ushort species, byte form)
    {
        if (species <= MaxSpecies && species < Table.Length)
            return Table[species].FormIndex(species, form);
        return 0;
    }

    public bool IsSpeciesInGame(ushort species) => species <= MaxSpecies && species > 0 && species < Table.Length;
    public bool IsPresentInGame(ushort species, byte form)
    {
        if (!IsSpeciesInGame(species))
            return false;
        if (form == 0)
            return true;
        if (Table[species].HasForm(form))
            return true;
        return false;
    }

    PersonalInfo IPersonalTable.this[int index] => this[index];
    PersonalInfo IPersonalTable.this[ushort species, byte form] => this[species, form];
    PersonalInfo IPersonalTable.GetFormEntry(ushort species, byte form) => GetFormEntry(species, form);
}