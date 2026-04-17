using skill_sts2.Scripts.CharacterConfigs;

namespace skill_sts2.Scripts.SkillSystem;

internal static class CharacterSkillRegistry
{
    private static readonly IReadOnlyDictionary<string, CharacterSkillProfile> _profiles = BuildProfiles();

    public static CharacterSkillProfile? Get(string? characterEntry)
    {
        if (string.IsNullOrWhiteSpace(characterEntry))
        {
            return null;
        }

        _profiles.TryGetValue(characterEntry.ToLowerInvariant(), out CharacterSkillProfile? profile);
        return profile;
    }

    private static IReadOnlyDictionary<string, CharacterSkillProfile> BuildProfiles()
    {
        ICharacterSkillProvider[] providers =
        {
            new IroncladSkillProvider(),
            new SilentSkillProvider(),
            new DefectSkillProvider(),
            new RegentSkillProvider(),
            new NecrobinderSkillProvider()
        };

        Dictionary<string, CharacterSkillProfile> result = new();
        foreach (ICharacterSkillProvider provider in providers)
        {
            CharacterSkillProfile profile = provider.Build();
            result[profile.CharacterEntry.ToLowerInvariant()] = profile;
        }

        return result;
    }
}
