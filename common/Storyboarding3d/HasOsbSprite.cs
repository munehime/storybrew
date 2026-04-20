using StorybrewCommon.Storyboarding;

namespace StorybrewCommon.Storyboarding3d
{
    public interface HasOsbSprites
    {
        IEnumerable<OsbSprite> Sprites { get; }
    }
}
