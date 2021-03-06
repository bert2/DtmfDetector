﻿namespace Unit {
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DtmfDetection;
    using Shouldly;
    using Xunit;

    using static DtmfDetection.DtmfGenerator;
    using static AnalyzerTestsExt;

    public class AnalyzerTests {
        [Fact]
        public void ReturnsStartAndStopOfSingleTone() =>
            Mark(PhoneKey.Five)
            .Process().AndIgnorePositions()
            .ShouldBe(new[] { Start(PhoneKey.Five), Stop(PhoneKey.Five) });

        [Fact]
        public void SignalsThatNoMoreDataIsAvailable() {
            var analyzer = Analyzer.Create(
                Generate(PhoneKey.Zero).Take(Config.DefaultSampleBlockSize).AsSamples(),
                Config.Default);
            _ = analyzer.AnalyzeNextBlock();

            _ = analyzer.AnalyzeNextBlock();

            analyzer.MoreSamplesAvailable.ShouldBeFalse();
        }

        [Fact]
        public void ReturnsStopOfCutOffTones() =>
            Mark(PhoneKey.Eight, ms: 24)
            .Process().AndIgnorePositions()
            .ShouldBe(new[] { Start(PhoneKey.Eight), Stop(PhoneKey.Eight) });

        [Fact]
        public void ReturnsStartAndStopOfMultipleTones() =>
            Concat(Space(), Mark(PhoneKey.A), Space(), Mark(PhoneKey.C), Space(), Mark(PhoneKey.A), Space(), Mark(PhoneKey.B), Space())
            .Process().AndIgnorePositions()
            .ShouldBe(new[] {
                Start(PhoneKey.A), Stop(PhoneKey.A),
                Start(PhoneKey.C), Stop(PhoneKey.C),
                Start(PhoneKey.A), Stop(PhoneKey.A),
                Start(PhoneKey.B), Stop(PhoneKey.B)
            });

        [Fact]
        public void ReturnsStartAndStopOfMultipleTonesAlignedWithSampleFrameSize() =>
            Concat(Mark(PhoneKey.A, ms: 26), Mark(PhoneKey.C, ms: 26), Mark(PhoneKey.A, ms: 26), Mark(PhoneKey.B, ms: 26))
            .Process().AndIgnorePositions()
            .ShouldBe(new[] {
                Start(PhoneKey.A), Stop(PhoneKey.A),
                Start(PhoneKey.C), Stop(PhoneKey.C),
                Start(PhoneKey.A), Stop(PhoneKey.A),
                Start(PhoneKey.B), Stop(PhoneKey.B)
            });

        [Fact]
        public void ReturnsStartAndStopOfMultipleOverlappingStereoTones() =>
            Stereo(
                left:  Concat(Mark(PhoneKey.A, ms: 80), Space(ms: 40), Mark(PhoneKey.C, ms: 80), Space(ms: 60)),
                right: Concat(Space(ms: 60), Mark(PhoneKey.B, ms: 80), Space(ms: 40), Mark(PhoneKey.D, ms: 80)))
            .Process(channels: 2).AndIgnorePositions()
            .ShouldBe(new[] {
                // left channel         // right channel
                Start(PhoneKey.A, 0),   Start(PhoneKey.B, 1),
                Stop(PhoneKey.A, 0),
                Start(PhoneKey.C, 0),   Stop(PhoneKey.B, 1),
                                        Start(PhoneKey.D, 1),
                Stop(PhoneKey.C, 0),    Stop(PhoneKey.D, 1)
            });

        [Fact]
        public void ThrowsWhenCreatedWithValidConfigButNullSamples() => new Action(() =>
            Analyzer.Create(samples: null!, Config.Default))
            .ShouldThrow<ArgumentNullException>();

        [Fact]
        public void ThrowsWhenCreatedWithValidDetectorButNullSamples() => new Action(() =>
            Analyzer.Create(samples: null!, new Detector(1, Config.Default)))
            .ShouldThrow<ArgumentNullException>();

        [Fact]
        public void ThrowsWhenCreatedWithNullDetector() => new Action(() =>
            Analyzer.Create(new AudioData(Array.Empty<float>(), 1, 8000), detector: null!))
            .ShouldThrow<ArgumentNullException>();

        [Fact]
        public void ThrowsWhenCreatedWithMismatchingSampleRateOfConfig() => new Action(() =>
            Analyzer.Create(new AudioData(Array.Empty<float>(), 1, 8000), Config.Default.WithSampleRate(16000)))
            .ShouldThrow<InvalidOperationException>();

        [Fact]
        public void ThrowsWhenCreatedWithMismatchingSampleRateOfDetector() => new Action(() =>
            Analyzer.Create(new AudioData(Array.Empty<float>(), 1, 8000), new Detector(1, Config.Default.WithSampleRate(16000))))
            .ShouldThrow<InvalidOperationException>();

        [Fact]
        public void ThrowsWhenCreatedWithMismatchingChannelsOfDetector() => new Action(() =>
            Analyzer.Create(new AudioData(Array.Empty<float>(), 1, 8000), new Detector(2, Config.Default)))
            .ShouldThrow<InvalidOperationException>();
    }

    public static class AnalyzerTestsExt {
        public static List<DtmfChange> Process(this IEnumerable<float> samples, int channels = 1) {
            var analyzer = Analyzer.Create(samples.AsSamples(channels), Config.Default);

            var dtmfs = new List<DtmfChange>();
            while (analyzer.MoreSamplesAvailable) dtmfs.AddRange(analyzer.AnalyzeNextBlock());

            return dtmfs;
        }

        public static IEnumerable<DtmfChange> AndIgnorePositions(this IEnumerable<DtmfChange> dtmfs) => dtmfs
            .Select(x => new DtmfChange(x.Key, new TimeSpan(), x.Channel, x.IsStart));

        public static DtmfChange Start(PhoneKey k, int channel = 0) => DtmfChange.Start(k, new TimeSpan(), channel);

        public static DtmfChange Stop(PhoneKey k, int channel = 0) => DtmfChange.Stop(k, new TimeSpan(), channel);
    }
}
