using Meitou.Data;
using Meitou.Data.Characters;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Save;
using Meitou.Simulation.Items;
using Meitou.Simulation.Saving;

/// <summary>
/// A feasibility probe against the installed release: identical equipment can retain independent dyes and appearance parts.
/// No colour feature or database edits are installed in the production mod by this check.
/// </summary>
internal static class ColourChecks
{
    internal static void Run()
    {
        var db = new GameDatabase();
        var records = new FcsFile();
        var race = new FcsRecord { StringId = "colour-race", RecordType = FcsRecordType.Race };
        var shirt = new FcsRecord { StringId = "colour-shirt", RecordType = FcsRecordType.Armour };
        shirt.Ints["slot"] = (int)AttachSlot.Shirt;
        shirt.Bools["dont colorise"] = false;
        shirt.Filenames["mesh"] = "probe.mesh";
        shirt.Filenames["vest texture"] = "probe.dds";
        shirt.Filenames["vest colormap"] = "probe-colour.dds";
        var red = new FcsRecord { StringId = "colour-red", RecordType = FcsRecordType.ColorData };
        red.Ints["color 1"] = 0xFF0000;
        red.Ints["color 2"] = 0xFFAA00;
        var blue = new FcsRecord { StringId = "colour-blue", RecordType = FcsRecordType.ColorData };
        blue.Ints["color 1"] = 0x0000FF;
        blue.Ints["color 2"] = 0x00AAFF;
        var faction = new FcsRecord { StringId = "colour-faction", RecordType = FcsRecordType.Faction };
        faction.References["color"] = [new FcsReference(red.StringId, 0, 0, 0)];
        records.Records.AddRange([race, shirt, red, blue, faction]);
        db.Apply(records, "colour-probe.mod");

        Inventory InventoryWith(string dye)
        {
            var inventory = new Inventory();
            inventory.Items.Add(new ItemInstance { Record = shirt.StringId, Type = FcsRecordType.Armour, Section = "shirt", ColourId = dye });
            return inventory;
        }
        var a = InventoryWith(red.StringId);
        var b = InventoryWith(blue.StringId);
        var file = new SaveFile();
        List<SaveItem> RoundTrip(Inventory inventory, string id)
        {
            var state = new FcsRecord { StringId = id, RecordType = FcsRecordType.InventoryState };
            file.Records.Add(state);
            SaveInventory.Replace(file, state, SavedKit.FromInventory(inventory), i => $"colour-item-{i}");
            var read = SaveFile.Read(file.ToBytes());
            return SaveInventory.Read(read, read.Records.Single(r => r.StringId == id));
        }
        var aSaved = RoundTrip(a, "inventory-a");
        var bSaved = RoundTrip(b, "inventory-b");
        Check(aSaved.Single().ColourId == red.StringId && bSaved.Single().ColourId == blue.StringId,
            "native binary inventory save preserves distinct dyes on identical item records");
        var appearance = new FcsRecord { RecordType = FcsRecordType.CharacterAppearance };
        appearance.References["race"] = [new FcsReference(race.StringId, 100, 0, 0)];
        CharacterAppearance Build(List<SaveItem> items)
        {
            var loadout = SavedKit.LoadoutOf(appearance, items, db, 1, db.Find(faction.StringId));
            return CharacterAppearance.Build(db, Path.GetTempPath(), race.StringId, new CharacterOptions { Loadout = loadout });
        }
        var aLook = Build(aSaved);
        var bLook = Build(bSaved);
        Check(aLook.Parts.Single(p => p.Record.StringId == shirt.StringId).Colour?.StringId == red.StringId
            && bLook.Parts.Single(p => p.Record.StringId == shirt.StringId).Colour?.StringId == blue.StringId,
            "same race and faction yield separate mesh part dyes");
        Check(aLook.BodyLayers.Single().Colour?.StringId == red.StringId && bLook.BodyLayers.Single().Colour?.StringId == blue.StringId,
            "shirt body layers also retain individual dyes");
        Check(ItemColours.Shown(db, db.Find(shirt.StringId)!, db.Find(blue.StringId), null, db.Find(faction.StringId))?.StringId == blue.StringId,
            "instance dye overrides shared faction colour");
        var protectedArmour = new FcsRecord { StringId = "colour-protected", RecordType = FcsRecordType.Armour };
        protectedArmour.Bools["dont colorise"] = true;
        var addition = new FcsFile();
        addition.Records.Add(protectedArmour);
        db.Apply(addition, "protected-probe.mod");
        Check(ItemColours.Shown(db, db.Find(protectedArmour.StringId)!, db.Find(blue.StringId), null, db.Find(faction.StringId)) is null,
            "dont colorise prevents displayed dye");
        Check(a.Items.Single().ColourId == red.StringId && db.Find(faction.StringId)!.GetReferences("color")[0].TargetStringId == red.StringId,
            "second character dye does not mutate first item or shared faction");
        Console.WriteLine("PASS: colour feasibility: identical gear, independent mesh/shirt dyes, binary inventory save, faction isolation and dont-colorise behavior.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
