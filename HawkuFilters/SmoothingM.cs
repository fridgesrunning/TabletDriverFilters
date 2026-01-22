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
            "Has multiple uses: acts as the extra frames option, and...\n" +
            "If you're using windows and using this after another async filter, good luck."
        )]
        public bool Wire { set; get; }

        [Property("Delta Override (Hover Over The Textbox)"), DefaultPropertyValue(0f), ToolTip
        (
            "Do NOT change this from 0 unless you know EXACTLY what you are doing."
        )]
        public float deltaOverride { set; get; }

        [Property("Print Update Delta"), DefaultPropertyValue(false), ToolTip
        (
            "Do NOT change unless you know EXACTLY what you are doing."
        )]
        public bool PUD { set; get; }


        protected override void ConsumeState()
        {
            if (State is ITabletReport report) {
                consumeDelta = (float)reportStopwatch.Restart().TotalMilliseconds;
                StatUpdate(report);
                if (report.Pressure > 499999) {
                    sharpd = true;
                    tpressure = report.Pressure - 500000;
                }
                else {
                    sharpd = false;
                    tpressure = report.Pressure;
                }
                this.targetPos = new Vector3(pos0, tpressure) * mmScale;


                consume = true;

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

             //   Console.WriteLine(report.Position - pos0);
                
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

                float mod1 = FSmoothstep(vel0, 0, 2) - FSmootherstep(vel0, 3, 4);

                float mod2 =  FSmoothstep(Math.Abs(accel0), 1, 0);

                float mod3 = FSmoothstep(vel0, 4, 0);

                float mod4 = FSmoothstep(accel0 + accel1, 0, -1);

                if (sharpd) {
                mod4 = 1;
                Console.WriteLine("a");
                }

                float power = Math.Max(1 - (0.5f * mod1 * mod2) - (mod3 * mod4), 0);
        

                updateMsAvg += ((updateDelta - updateMsAvg) * 0.1f);
                Vector3 delta = point - this.lastPos;

                if (consume) {
                    
                    initDelta = delta;
                    consume = false;
                }


                float currWeight = weight;
                if (Wire) {
                    if (deltaOverride == 0 | Math.Abs(deltaOverride - updateDelta) < 0.25f)
                    currWeight *= (updateDelta / updateMsAvg) * updateMsAvg;
                    else { 
                            currWeight *= (updateDelta / updateMsAvg) * updateMsAvg;
              
                       
                            if (updateDelta > 0.6f) {
                                currWeight *= deltaOverride;

                            if (PUD)
                                Console.WriteLine(updateDelta);
                            }
                        
                   }
                }

                    this.lastPos += delta * MathF.Pow(currWeight, power);

              //      Console.WriteLine(MathF.Pow(currWeight, power));
                    
                //Console.WriteLine(updateMsAvg);
                
                
                  

                return this.lastPos;

            }
        }

        public static float FSmoothstep(float x, float start, float end)
        {
            x = (float)Math.Clamp((x - start) / (end - start), 0.0, 1.0);
            return x * x * (3 - 2 * x);
        }

        public static float FSmootherstep(float x, float start, float end)
        {
            x = (float)Math.Clamp((x - start) / (end - start), 0.0, 1.0);
            return (float)(x * x * x * (x * (6.0 * x - 15.0) + 10.0));
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

        void StatUpdate(ITabletReport report) {
            pos2 = pos1;
            pos1 = pos0;
            pos0 = report.Position;

            dir3 = dir2;
            dir2 = dir1;
            dir1 = dir0;
            dir0 = (pos0 - pos1);

            vel2 = vel1;
            vel1 = vel0;
            vel0 = dir0.Length();

            accel1 = accel0;
            accel0 = (vel0 - vel1);

            ddir1 = ddir0;
            ddir0 = (dir0 - dir1);

            pointaccel1 = pointaccel0;
            pointaccel0 = ddir0.Length(); 
        }

        Vector2 pos0, pos1, pos2, dir0, dir1, dir2, dir3, ddir0, ddir1;
        float vel0, vel1, vel2, accel0, accel1, pointaccel0, pointaccel1;

        private HPETDeltaStopwatch reportStopwatch = new HPETDeltaStopwatch();
        private HPETDeltaStopwatch updateStopwatch = new HPETDeltaStopwatch();

        private const float THRESHOLD = 0.63f;
        private float timerInterval => 1000 / Frequency;

        private float weight;
        private Vector3 mmScale;
        private Vector3 targetPos;
        private Vector3 lastPos;

        float consumeDelta = 5;
        float updateDelta = 1;
        float updateMsAvg = 1;

        int wait;

        private Vector3 LUPOC0, LUPOC1;
        private Vector3 initDelta;

        bool consume;
        uint tpressure;
        bool sharpd;

        
    }
}
