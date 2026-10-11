using System;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace PKHeX.Core;

/// <summary>
/// Save file handler for Relumi (pbrelumi) JSON-based save format.
/// Pokémon data within the JSON is stored as BDSP-encoded (PB8) encrypted byte arrays.
///
/// Internally maintains a synthetic byte buffer in the standard BDSP layout
/// (party then boxes, each slot SIZE_PARTY bytes) so the existing SaveFile
/// read/write plumbing works transparently.
/// </summary>
public sealed class SAVRelumi : SaveFile
{
    // JSON path constants (keys are nested, NOT dot-notation)
    private const string PartyKey = "playerParty";
    private const string MembersKey = "members";
    private const string BoxTrayKey = "boxTray";
    private const string PokemonParamKey = "pokemonParam";
    private const string BufferKey = "buffer";

    // Parsed JSON tree — kept for lossless round-trip save-back
    private readonly JsonObject _json;
    private readonly JsonObject _saveData;
    private readonly byte[] _pkmBuffer;  // synthetic party+box buffer

    public override string Extension => ".pbrelumi";

    public SAVRelumi(Memory<byte> data) : base(data)
    {
        // Parse JSON from the UTF-8 buffer (use Encoding.UTF8, NOT GetString which is for Pokémon text)
        var jsonText = Encoding.UTF8.GetString(Data);
        var root = JsonNode.Parse(jsonText)?.AsObject()
            ?? throw new InvalidOperationException("Invalid JSON in save file.");
        _json = root;
        _saveData = root["playerWork"]?["_saveData"]?.AsObject()
            ?? throw new InvalidOperationException("Missing playerWork._saveData in save JSON.");

        // Determine box count from boxTray array
        var boxTrayArr = _saveData[BoxTrayKey]?.AsArray()
            ?? throw new InvalidOperationException("Missing boxTray array.");
        BoxCount = boxTrayArr.Count;

        // Allocate synthetic buffer: [6 party slots] + [N boxes × 30 slots], each SIZE_PARTY bytes
        int pkmBufferLength = (6 * SIZE_PARTY) + (BoxCount * BoxSlotCount * SIZE_PARTY);
        _pkmBuffer = new byte[pkmBufferLength];

        ReloadMonBuffer();
    }

    /// <summary> Rebuilds the synthetic mon buffer from the current JSON tree. </summary>
    private void ReloadMonBuffer()
    {
        Array.Clear(_pkmBuffer);
        PartyCount = 0;

        // Load party Pokémon encrypted blobs into the buffer
        LoadPartyFromJson();

        // Load box Pokémon encrypted blobs into the buffer
        var boxTrayArr = _saveData[BoxTrayKey]?.AsArray();
        if (boxTrayArr is not null)
            LoadBoxesFromJson(boxTrayArr);
    }

    /// <summary>
    /// Absorbs edits from another <see cref="SAVRelumi"/> (e.g. the Trainer Info dialog's clone).
    /// Raw <see cref="SaveFile.Data"/> is just the original file bytes; live state is the JSON
    /// tree, so the base byte-copy would silently drop every edit.
    /// </summary>
    public override void CopyChangesFrom(SaveFile sav)
    {
        if (sav is not SAVRelumi r || ReferenceEquals(this, r))
            return;
        foreach (var kvp in r._saveData)
            _saveData[kvp.Key] = kvp.Value?.DeepClone();
        ReloadMonBuffer();
        State.Edited = true;
    }

    /// <summary> Reads party Pokémon from JSON into the synthetic buffer. </summary>
    private void LoadPartyFromJson()
    {
        var members = _saveData[PartyKey]?[MembersKey]?.AsArray();
        if (members is null) return;

        int count = Math.Min(members.Count, 6);
        for (int i = 0; i < count; i++)
        {
            var jsonBuf = members[i]?[BufferKey]?.AsArray();
            if (jsonBuf is null) continue;

            var dest = _pkmBuffer.AsSpan(i * SIZE_PARTY, SIZE_PARTY);
            WriteBufferToSpan(jsonBuf, dest);

            // Update PartyCount based on whether slot is occupied
            if (EntityDetection.IsPresent(dest))
                PartyCount = i + 1;
        }
    }

    /// <summary> Reads box Pokémon from JSON into the synthetic buffer. </summary>
    private void LoadBoxesFromJson(JsonArray boxTrayArr)
    {
        for (int box = 0; box < BoxCount; box++)
        {
            var boxObj = boxTrayArr[box]?.AsObject();
            if (boxObj is null) continue;

            var pokemonParam = boxObj[PokemonParamKey]?.AsArray();
            if (pokemonParam is null) continue;

            int offset = GetBoxOffset(box);
            int count = Math.Min(pokemonParam.Count, BoxSlotCount);
            for (int slot = 0; slot < count; slot++)
            {
                var jsonBuf = pokemonParam[slot]?[BufferKey]?.AsArray();
                if (jsonBuf is null) continue;

                var dest = _pkmBuffer.AsSpan(offset + (slot * SIZE_PARTY), SIZE_PARTY);
                WriteBufferToSpan(jsonBuf, dest);
            }
        }
    }

    /// <summary> Copies byte values from a JsonArray into a Span. </summary>
    private static void WriteBufferToSpan(JsonArray jsonArr, Span<byte> dest)
    {
        int len = Math.Min(jsonArr.Count, dest.Length);
        for (int i = 0; i < len; i++)
            dest[i] = (byte)jsonArr[i]!.GetValue<int>();
    }

    protected override Memory<byte> GetFinalData()
    {
        // Write party Pokémon back into the JSON
        WritePartyToJson();

        // Write box Pokémon back into the JSON
        WriteBoxesToJson();

        // Serialize JSON tree back to bytes
        var options = new JsonSerializerOptions { WriteIndented = false };
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(_json, options);
        return jsonBytes;
    }

    private void WritePartyToJson()
    {
        var members = _saveData[PartyKey]?[MembersKey]?.AsArray();
        if (members is null) return;

        int count = Math.Min(members.Count, 6);
        for (int i = 0; i < count; i++)
        {
            var src = _pkmBuffer.AsSpan(i * SIZE_PARTY, SIZE_PARTY);
            if (members[i] is JsonObject member)
                member[BufferKey] = EncodeBufferToArray(src);
        }
    }

    private void WriteBoxesToJson()
    {
        var boxTrayArr = _saveData[BoxTrayKey]?.AsArray();
        if (boxTrayArr is null) return;

        for (int box = 0; box < BoxCount; box++)
        {
            if (boxTrayArr[box] is not JsonObject boxObj) continue;
            var pokemonParam = boxObj[PokemonParamKey]?.AsArray();
            if (pokemonParam is null) continue;

            int offset = GetBoxOffset(box);
            int count = Math.Min(pokemonParam.Count, BoxSlotCount);
            for (int slot = 0; slot < count; slot++)
            {
                var src = _pkmBuffer.AsSpan(offset + (slot * SIZE_PARTY), SIZE_PARTY);
                if (pokemonParam[slot] is JsonObject slotObj)
                    slotObj[BufferKey] = EncodeBufferToArray(src);
            }
        }
    }

    /// <summary> Encodes a raw 344-byte buffer into a JSON integer array. </summary>
    private static JsonArray EncodeBufferToArray(ReadOnlySpan<byte> data)
    {
        var arr = new JsonArray();
        for (int i = 0; i < data.Length; i++)
            arr.Add((int)data[i]);
        return arr;
    }

    // ── SaveFile identity ──────────────────────────────────────────────

    public override GameVersion Version { get => GameVersion.BDSP; set { } }
    public override byte Generation => 8;
    public override EntityContext Context => EntityContext.Gen8b;
    public override string GetString(ReadOnlySpan<byte> data) => StringConverter8.GetString(data);
    public override int LoadString(ReadOnlySpan<byte> data, Span<char> destBuffer) => StringConverter8.LoadString(data, destBuffer);
    public override int SetString(Span<byte> destBuffer, ReadOnlySpan<char> value, int maxLength, StringConverterOption option) => StringConverter8.SetString(destBuffer, value, maxLength, option);

    // ── Pokémon format ─────────────────────────────────────────────────

    public override Type PKMType => typeof(PB8R);
    public override PB8 BlankPKM => new PB8R();
    public override int SIZE_STORED => PokeCrypto.SIZE_8STORED;
    public override int SIZE_PARTY => PokeCrypto.SIZE_8PARTY;
    public override int SIZE_BOXSLOT => PokeCrypto.SIZE_8PARTY;
    public override int MaxEV => EffortValues.Max252;

    // ── Game limits ───────────────────────────────────────────────────

    public override ushort MaxMoveID => Legal.MaxMoveID_9;
    public override ushort MaxSpeciesID => Legal.MaxSpeciesID_9;
    public override int MaxAbilityID => Legal.MaxAbilityID_9;
    public override int MaxItemID => Legal.MaxItemID_9a;
    public override int MaxBallID => Legal.MaxBallID_9;
    public override GameVersion MaxGameID => Legal.MaxGameID_HOME2;
    public override PersonalTableRelumi Personal => PersonalTable.Relumi;

    // SV held items (gen 8-9) plus ZA mega stones and primal orbs.
    private static readonly ushort[] HeldItemsRelumi = BuildHeldItemsRelumi();

    private static ushort[] BuildHeldItemsRelumi()
    {
        var sv = Legal.HeldItems_SV;
        var extra = ItemStorage9ZA.GetAllUniqueHeldItems();
        var result = new ushort[sv.Length + extra.Length];
        Array.Copy(sv, result, sv.Length);
        Array.Copy(extra, 0, result, sv.Length, extra.Length);
        return result;
    }

    public override ReadOnlySpan<ushort> HeldItems => HeldItemsRelumi;
    public override int MaxStringLengthTrainer => 12;
    public override int MaxStringLengthNickname => 12;

    // ── Storage ────────────────────────────────────────────────────────

    public override int BoxCount { get; }
    public override int BoxSlotCount => 30;

    /// <summary> Party buffer points to first 6 slots of the synthetic buffer. </summary>
    protected override Span<byte> PartyBuffer => _pkmBuffer;

    /// <summary> Box buffer is the entire synthetic buffer; box offsets are placed after party. </summary>
    protected override Span<byte> BoxBuffer => _pkmBuffer;

    /// <summary> Party slot offset: 0..5 × SIZE_PARTY. </summary>
    public override int GetPartyOffset(int slot) => slot * SIZE_PARTY;

    /// <summary> Box offset: starts after the 6 party slots. </summary>
    public override int GetBoxOffset(int box) => (6 * SIZE_PARTY) + (box * BoxSlotCount * SIZE_PARTY);

    public override StorageSlotSource GetBoxSlotFlags(int index) => StorageSlotSource.None;
    public override bool HasParty => true;
    public override bool HasBox => true;

    // ── PKM decode/encode (BDSP path) ──────────────────────────────────

    protected override PKM GetPKM(Memory<byte> data) => new PB8R(data);
    protected override void DecryptPKM(Span<byte> data) => PokeCrypto.Decrypt8(data);

    // Relumi suppresses legality (HaX), so the editor's SavePartyStats skips the
    // Stat_Level ← CurrentLevel sync. Ensure the party-stats level matches.
    // Relumi shows level 0 for 0 EXP (its level-1 threshold is above 0, unlike standard
    // tables where 0 EXP is level 1). Floor fresh mons so new ones display level 1 in-game.
    // Assumption: the threshold is 1 EXP — fits all observed save data (lowest nonzero EXP
    // is 125); raise this if the game reveals a higher level-1 requirement.
    internal const uint MinimumEXP = 1;
    protected override void SetPartyValues(PKM pk, bool isParty)
    {
        base.SetPartyValues(pk, isParty);
        if (isParty)
            pk.Stat_Level = pk.CurrentLevel;
        // Floor EXP so new mons don't display as level 0 in Relumi (eggs and empty slots untouched).
        if (pk.Species != 0 && !pk.IsEgg && pk.EXP < MinimumEXP)
            pk.EXP = MinimumEXP;
    }

    // ── Trainer info (backed by playerData in the JSON) ──────────────────

    // PlayerData holds mystatus (name/id/gold/sex/badge), config (msg_lang_id), and playtime.
    private JsonObject? PlayerData => _saveData["playerData"]?.AsObject();
    private JsonObject? MyStatus => PlayerData?["mystatus"]?.AsObject();
    private JsonObject? Config => PlayerData?["config"]?.AsObject();
    private JsonObject? Playtime => PlayerData?["playtime"]?.AsObject();

    public override string OT
    {
        get => MyStatus?["name"]?.GetValue<string>() ?? string.Empty;
        set { if (MyStatus is { } m) m["name"] = value; }
    }

    // sex: false = male (0), true = female (1).
    public override byte Gender
    {
        get => MyStatus?["sex"]?.GetValue<bool>() == true ? (byte)1 : (byte)0;
        set { if (MyStatus is { } m) m["sex"] = value != 0; }
    }

    // Full 32-bit trainer ID; the dialog edits its low/high halves.
    private uint TrainerID32
    {
        get => MyStatus?["id"]?.GetValue<uint>() ?? 0;
        set { if (MyStatus is { } m) m["id"] = value; }
    }

    public override ushort TID16
    {
        get => (ushort)(TrainerID32 & 0xFFFF);
        set => TrainerID32 = (TrainerID32 & 0xFFFF0000) | value;
    }

    public override ushort SID16
    {
        get => (ushort)(TrainerID32 >> 16);
        set => TrainerID32 = (TrainerID32 & 0xFFFF) | ((uint)value << 16);
    }

    public override uint Money
    {
        get => (uint)(MyStatus?["gold"]?.GetValue<int>() ?? 0);
        set { if (MyStatus is { } m) m["gold"] = (int)value; }
    }

    public int Badges
    {
        get => MyStatus?["badge"]?.GetValue<int>() ?? 0;
        set { if (MyStatus is { } m) m["badge"] = value; }
    }

    // msg_lang_id uses standard LanguageID values (2 = English).
    public override int Language
    {
        get => Config?["msg_lang_id"]?.GetValue<int>() ?? (int)LanguageID.English;
        set { if (Config is { } c) c["msg_lang_id"] = value; }
    }

    public override int PlayedHours
    {
        get => Playtime?["hour"]?.GetValue<int>() ?? 0;
        set { if (Playtime is { } p) p["hour"] = value; }
    }

    public override int PlayedMinutes
    {
        get => Playtime?["minute"]?.GetValue<int>() ?? 0;
        set { if (Playtime is { } p) p["minute"] = value; }
    }

    public override int PlayedSeconds
    {
        get => Playtime?["second"]?.GetValue<int>() ?? 0;
        set { if (Playtime is { } p) p["second"] = value; }
    }

    // ── Checksums (no-op for JSON) ─────────────────────────────────────

    public override bool ChecksumsValid => true;
    public override string ChecksumInfo => string.Empty;
    protected override void SetChecksums() { }

    // ── Clone / summary ────────────────────────────────────────────────

    protected override SAVRelumi CloneInternal() => new(Data.ToArray());
    protected internal override string ShortSummary => $"Relumi - {PartyCount} party, {BoxCount}×{BoxSlotCount} boxes";
}