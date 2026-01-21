using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Timing;

namespace TabletDriverFilters.Hawku
{
    [PluginName("Hawku Smoothing Filter (M)")]
    public class SmoothingM : MillimeterAsyncPositionedPipelineElement
    {
        public override PipelinePosition Position => PipelinePosition.PostTransform;

        [SliderProperty("Latency", 0.0f, 1000.0f, 2.0f), DefaultPropertyValue(2f)]
        [ToolTip(
              "Smoothing Filter\n"
            + " - Smoothing filter adds latency to the input, so don't enable it if you want the lowest possible input latency.\n"
            + "\n"
            + "Recommendations\n"
            + " - On Wacom tablets you can use latency value between 15 and 25 to have a similar smoothing as in the Wacom drivers.\n"
            + " - You can test out different filter values, but recommended maximum for osu! is around 50 milliseconds.\n"
            + " - Filter latency value lower than 4 milliseconds isn't recommended. Its better to just disable the smoothing filter.\n"
            + " - You don't have to change the filter frequency, but you can use the highest frequency your computer can run without performance problems."
        )]
        public float Latency { set; get; }

        [Property("Wire"), DefaultPropertyValue(true), ToolTip
        (
            "Has multiple uses: acts as the extra frames option, also ensures secure wiring after another async filter when set to 0hz."
        )]
        public bool Wire { set; get; }


        protected override void ConsumeState()
        {
            if (State is ITabletReport report) {
                consumeDelta = (float)reportStopwatch.Restart().TotalMilliseconds;
                pos0 = report.Position;
                this.targetPos = new Vector3(pos0, report.Pressure) * mmScale;

                if (Wire)
                    UpdateState();
            }
            else
                OnEmit();
        }

        protected override void UpdateState()
        {
            if (State is ITabletReport report && PenIsInRange())
            {
                var newPoint = Filter(this.targetPos) / mmScale;
                report.Position = new Vector2(newPoint.X, newPoint.Y);
                Console.WriteLine(report.Position - pos0);
                report.Pressure = (uint)newPoint.Z;
                State = report;

                OnEmit();
            }
        }

        public Vector3 Filter(Vector3 point)
        {
            updateDelta = (float)updateStopwatch.Restart().TotalMilliseconds;
            // If a time difference hasn't been established or it has been 100 milliseconds since the last filter
            if (updateDelta == 0 || updateDelta > 100)
            {
                this.lastPos = point;
                SetWeight(Latency);
                return point;
            }
            else
            {
                updateMsAvg += ((updateDelta - updateMsAvg) * 0.1f);
                Vector3 delta = point - this.lastPos;
                float currWeight = weight;
                if (Wire && Frequency > 0)
                    currWeight *= (updateDelta / updateMsAvg) * updateMsAvg;
                this.lastPos += delta * currWeight;
                Console.WriteLine(updateMsAvg);
                return this.lastPos;

            }
        }

        private void SetWeight(float latency)
        {
            float stepCount;
            if (!Wire)
                stepCount = latency / timerInterval;
            else stepCount = latency;
            float target = 1 - THRESHOLD;
            this.weight = 1f - (1f / MathF.Pow(1f / target, 1f / stepCount));
        }

        protected override void HandleTabletReference(TabletReference tabletReference)
        {
            var digitizer = tabletReference.Properties.Specifications.Digitizer;
            this.mmScale = new Vector3
            {
                X = digitizer.Width / digitizer.MaxX,
                Y = digitizer.Height / digitizer.MaxY,
                Z = 1  // passthrough
            };
        }

        Vector2 pos0;

        private HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();
        private HPETDeltaStopwatch updateStopwatch = new HPETDeltaStopwatch();

        private const float THRESHOLD = 0.9f;
        private float timerInterval => 1000 / Frequency;

        private float weight;
        private Vector3 mmScale;
        private Vector3 targetPos;
        private Vector3 lastPos;

        float consumeDelta = 0;
        float updateDelta = 0;
        float updateMsAvg = 1;

        
    }
}
