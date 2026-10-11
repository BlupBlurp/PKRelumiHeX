using System;
using System.IO;
using Xunit;

namespace PKHeX.Core.Tests;

public class SAVRelumiTests
{
    // SaveData.bin lives at repo root.
    // GetRepoPath returns Tests/PKHeX.Core.Tests when cwd is there, so go up two levels.
    private static string SavePath =>
        Path.GetFullPath(Path.Combine(TestUtil.GetRepoPath(), "..", "..", "SaveData.bin"));

    [Fact]
    public void LoadAndRoundTrip()
    {
        Assert.True(File.Exists(SavePath), $"Save file not found: {Path.GetFullPath(SavePath)}");
        var originalBytes = File.ReadAllBytes(SavePath);

        // Load
        var reader = new PbrelumiReader();
        Assert.True(reader.TryRead(originalBytes, out var sav), "Failed to load save");
        var relumi = (SAVRelumi)sav;
        Assert.True(relumi.PartyCount > 0, "Should have party Pokémon");
        Assert.Equal(80, relumi.BoxCount);

        // Check we can read some data
        var pk0 = relumi.GetPartySlotAtIndex(0);
        Assert.NotEqual(0, pk0.Species);

        // Save without edits
        var result = relumi.Write();
        var resultBytes = result.ToArray();

        // Compare byte-for-byte
        Assert.Equal(originalBytes.Length, resultBytes.Length);
        Assert.True(originalBytes.AsSpan().SequenceEqual(resultBytes),
            "Round-trip should produce byte-identical output when no edits made");
    }

    [Fact]
    public void PersonalTableHasRelumiAbilities()
    {
        // Croconaw (159) is changed in Relumi: Strong Jaw / Torrent / Sheer Force.
        var pi = PersonalTable.Relumi.GetFormEntry(159, 0);
        Assert.Equal(173, pi.GetAbilityAtIndex(0)); // Strong Jaw
        Assert.Equal(67, pi.GetAbilityAtIndex(1));  // Torrent
        Assert.Equal(125, pi.GetAbilityAtIndex(2)); // Sheer Force
    }

    [Fact]
    public void PersonalTableGen5EntriesUseSVData()
    {
        // Species 494-559 must be synthesized from SV (not BDSP form entries).
        // Victini (494) yields 3 HP EVs; Snivy (495) yields 1 Speed EV.
        var victini = PersonalTable.Relumi.GetFormEntry(494, 0);
        Assert.Equal(3, victini.EV_HP);
        Assert.Equal(0, victini.EV_ATK);

        var snivy = PersonalTable.Relumi.GetFormEntry(495, 0);
        Assert.Equal(1, snivy.EV_SPE);
    }

    [Fact]
    public void RelumiFormNamesHasCustomMewtwoForms()
    {
        // Mewtwo (150) has 7 forms in Relumi, including the armored/shadow customs.
        var names = RelumiFormNames.GetFormNames(150);
        Assert.NotNull(names);
        Assert.Equal(7, names.Length);
        Assert.Equal("Mega X Mewtwo", names[1]);
        Assert.Equal("Mega Y Mewtwo", names[2]);
        Assert.Equal("Mk II Armored Mewtwo", names[3]);
        Assert.Equal("Mk I Armored Mewtwo", names[4]);
        Assert.Equal("Shadow Mewtwo", names[5]);
        Assert.Equal("Shadow Mega Mewtwo", names[6]);
    }

    [Fact]
    public void RelumiFormNamesMatchesPersonalFormCounts()
    {
        // Every form list must line up with the personal table's FormCount.
        for (ushort species = 1; species <= PersonalTable.Relumi.MaxSpeciesID; species++)
        {
            var pi = PersonalTable.Relumi[species];
            var names = RelumiFormNames.GetFormNames(species);
            if (pi.FormCount <= 1)
            {
                Assert.Null(names);
            }
            else
            {
                Assert.NotNull(names);
                Assert.Equal(pi.FormCount, names.Length);
            }
        }
    }

    [Fact]
    public void BlankPB8RLevel1EncryptsToCorrectBytes()
    {
        // A level-1 PB8R with EXP=0 must produce Stat_Level=1 in the encrypted blob.
        var pk = new PB8R
        {
            Species = 1,   // Bulbasaur
            Stat_Level = 1, // as set by the level textbox (LoadSave.cs)
        };
        pk.ResetPartyStats();

        // Write encrypted to a buffer, matching what WriteSlotParty does internally.
        var buffer = new byte[pk.SIZE_PARTY];
        pk.WriteEncryptedDataParty(buffer);

        // Decrypt and verify.
        PokeCrypto.Decrypt8(buffer);
        Assert.Equal(1, buffer[0x148]); // Stat_Level at G8PKM offset
        Assert.Equal(0u, BitConverter.ToUInt32(buffer.AsSpan(0x28))); // EXP at PB8 offset
    }

    [Fact]
    public void NewBoxMonGetsLevelOneExpFloor()
    {
        // Relumi displays level 0 for 0 EXP; saving must floor fresh mons so they show level 1.
        Assert.True(File.Exists(SavePath));
        var originalBytes = File.ReadAllBytes(SavePath);
        var reader = new PbrelumiReader();
        Assert.True(reader.TryRead(originalBytes, out var sav));
        var relumi = (SAVRelumi)sav;

        var pk = (PB8R)relumi.BlankPKM;
        pk.Species = 216; // Teddiursa
        pk.Stat_Level = 1;
        pk.EXP = 0;
        pk.ResetPartyStats();
        pk.RefreshChecksum();
        relumi.SetBoxSlotAtIndex(pk, 0, 17); // empty slot in the sample save

        var resultBytes = relumi.Write().ToArray();
        Assert.True(reader.TryRead(resultBytes, out var sav2));
        var back = ((SAVRelumi)sav2).GetBoxSlotAtIndex(0, 17);
        Assert.Equal((ushort)216, back.Species);
        Assert.Equal(1u, back.EXP); // floored, still level 1 in every standard curve
        Assert.Equal(1, back.CurrentLevel);
    }

    [Fact]
    public void TrainerInfoReadsSaveValues()
    {
        // Sample save: name Bello, id 3682250500, gold 39492, male, 1 badge, English, 6:48:13.
        Assert.True(File.Exists(SavePath));
        var reader = new PbrelumiReader();
        Assert.True(reader.TryRead(File.ReadAllBytes(SavePath), out var sav));
        var relumi = (SAVRelumi)sav;

        Assert.Equal("Bello", relumi.OT);
        Assert.Equal(44804, relumi.TID16); // 3682250500 & 0xFFFF
        Assert.Equal(56186, relumi.SID16); // 3682250500 >> 16
        Assert.Equal(39492u, relumi.Money);
        Assert.Equal(0, relumi.Gender);
        Assert.Equal(1, relumi.Badges);
        Assert.Equal(2, relumi.Language); // English
        Assert.Equal(6, relumi.PlayedHours);
        Assert.Equal(48, relumi.PlayedMinutes);
        Assert.Equal(13, relumi.PlayedSeconds);
    }

    [Fact]
    public void TrainerInfoEditRoundTrips()
    {
        Assert.True(File.Exists(SavePath));
        var reader = new PbrelumiReader();
        Assert.True(reader.TryRead(File.ReadAllBytes(SavePath), out var sav));
        var relumi = (SAVRelumi)sav;

        relumi.OT = "Testy";
        relumi.TID16 = 12345;
        relumi.SID16 = 54321;
        relumi.Money = 123456;
        relumi.Gender = 1;
        relumi.Badges = 0xFF;
        relumi.Language = 1; // Japanese
        relumi.PlayedHours = 10;
        relumi.PlayedMinutes = 20;
        relumi.PlayedSeconds = 30;

        var resultBytes = relumi.Write().ToArray();
        Assert.True(reader.TryRead(resultBytes, out var sav2));
        var back = (SAVRelumi)sav2;
        Assert.Equal("Testy", back.OT);
        Assert.Equal(12345, back.TID16);
        Assert.Equal(54321, back.SID16);
        Assert.Equal(123456u, back.Money);
        Assert.Equal(1, back.Gender);
        Assert.Equal(0xFF, back.Badges);
        Assert.Equal(1, back.Language);
        Assert.Equal(10, back.PlayedHours);
        Assert.Equal(20, back.PlayedMinutes);
        Assert.Equal(30, back.PlayedSeconds);
    }

    [Fact]
    public void TrainerInfoCloneCopyTransfersEdits()
    {
        // The Trainer Info dialog edits a clone, then copies back; the base
        // byte-copy would drop JSON-tree edits, so Relumi overrides it.
        Assert.True(File.Exists(SavePath));
        var reader = new PbrelumiReader();
        Assert.True(reader.TryRead(File.ReadAllBytes(SavePath), out var sav));
        var relumi = (SAVRelumi)sav;

        var clone = (SAVRelumi)relumi.Clone();
        clone.OT = "ViaClone";
        clone.Language = 1;
        clone.Badges = 0xFF;
        relumi.CopyChangesFrom(clone);

        Assert.Equal("ViaClone", relumi.OT);
        var resultBytes = relumi.Write().ToArray();
        Assert.True(reader.TryRead(resultBytes, out var sav2));
        var back = (SAVRelumi)sav2;
        Assert.Equal("ViaClone", back.OT);
        Assert.Equal(1, back.Language);
        Assert.Equal(0xFF, back.Badges);
    }

    [Fact]
    public void EditAndReSave()
    {
        Assert.True(File.Exists(SavePath));
        var originalBytes = File.ReadAllBytes(SavePath);

        // Load
        var reader = new PbrelumiReader();
        Assert.True(reader.TryRead(originalBytes, out var sav));
        var relumi = (SAVRelumi)sav;

        // Read original move
        var pk0 = relumi.GetPartySlotAtIndex(0);
        var originalMove = pk0.Move1;
        var newMove = originalMove == (ushort)1 ? (ushort)33 : (ushort)1;

        // Edit and save
        pk0.Move1 = newMove;
        pk0.RefreshChecksum();
        relumi.SetPartySlotAtIndex(pk0, 0);

        var result = relumi.Write();
        var resultBytes = result.ToArray();

        // Reload and verify the edit persisted
        Assert.True(reader.TryRead(resultBytes, out var sav2));
        var pk0b = sav2.GetPartySlotAtIndex(0);
        Assert.Equal(newMove, pk0b.Move1);

        // The edited file must differ in content (length can coincide when both values use the same digit count).
        Assert.False(originalBytes.AsSpan().SequenceEqual(resultBytes), "Edited save should differ from original");
        Assert.True(pk0b.Move1 == newMove);
    }
}