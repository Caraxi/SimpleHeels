using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace SimpleHeels;

public class GroupCharacter {
    public string Name = string.Empty;
    public uint World = ushort.MaxValue;
}

public class GroupConfig : CharacterConfig {
    public List<GroupCharacter> Characters = new();
    public HashSet<uint> Clans = new();
    public string Label = "New Group";
    public bool MatchFeminine = true;
    public bool MatchMasculine = true;

    public unsafe bool Matches(DrawObject* drawObject, string name, uint world) {
        if (!Enabled) return false;
        if (drawObject == null) return false;
        if (drawObject->Object.GetObjectType() != ObjectType.CharacterBase) return false;
        var characterBase = (CharacterBase*)drawObject;
        if (characterBase->GetModelType() != CharacterBase.ModelType.Human) return false;
        var human = (Human*)characterBase;
        if (human->Customize.Sex > 1) return false;
        if (human->Customize.Sex == 0 && MatchMasculine == false) return false;
        if (human->Customize.Sex == 1 && MatchFeminine == false) return false;
        if (Clans.Count >= 1 && Clans.All(c => c != human->Customize.Clan)) return false;

        if (Characters.Any(c => !string.IsNullOrWhiteSpace(c.Name)) && !Characters.Any(c => c.World == world && c.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))) return false;

        return true;
    }
}
