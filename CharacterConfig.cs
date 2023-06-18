using System.Collections.Generic;
using System.Numerics;

namespace SimpleHeels; 

public class CharacterConfig {
    public List<HeelConfig> HeelsConfig = new();

    public float SittingOffsetZ = 0f;
    public float SittingOffsetY = 0f;
}
