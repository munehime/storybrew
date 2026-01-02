namespace StorybrewCommon.Mapset
{
    public interface OsuSamplePoint
    {
        double SampleTime { get; }
        HitSoundAddition Additions { get; }
        SampleSet SampleSet { get; }
        SampleSet AdditionsSampleSet { get; }
        int CustomSampleSet { get; }
        float Volume { get; }
    }
}
