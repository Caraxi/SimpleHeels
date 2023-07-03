using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace SimpleHeels;

public class GroupConfig : CharacterConfig {
    public List<uint> Clans = new List<uint>();
    public bool MatchMasculine = true;
    public bool MatchFeminine = true;
    public string Label = "New Group";
    public unsafe bool Matches(DrawObject* drawObject) {
        if (drawObject == null) return false;
        if (drawObject->Object.GetObjectType() != ObjectType.CharacterBase) return false;
        var characterBase = (CharacterBase*)drawObject;
        if (characterBase->GetModelType() != CharacterBase.ModelType.Human) return false;
        var human = (Human*)characterBase;
        if (human->Customize.Sex > 1) return false;
        if (human->Customize.Sex == 0 && MatchMasculine == false) return false;
        if (human->Customize.Sex == 1 && MatchFeminine == false) return false;
        if (Clans.Count >= 1 && Clans.All(c => c != human->Customize.Clan)) return false;
        return true;
    }
}
