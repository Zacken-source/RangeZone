using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo
{
    // Finds consolidation ranges and liquidity levels (equal highs / lows,
    // range edges), then marks sweeps of those levels.
    // A sweep = wick through the level but close back inside.
    // It's confirmed if the next N candles don't close back through the level
    // and the Nth candle closes further away than the sweep candle.
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class RangeZone : Indicator
    {
        [Parameter("Bars to scan", DefaultValue = 1000, MinValue = 100, Group = "General")]
        public int BarsToScan { get; set; }

        [Parameter("Max range height (ATR)", DefaultValue = 2.5, MinValue = 0.5, Group = "Ranges")]
        public double MaxHeightAtr { get; set; }

        [Parameter("Min bars in range", DefaultValue = 20, MinValue = 5, Group = "Ranges")]
        public int MinBars { get; set; }

        [Parameter("Ranges to show", DefaultValue = 8, MinValue = 1, Group = "Ranges")]
        public int RangesToShow { get; set; }

        [Parameter("Color", DefaultValue = "SkyBlue", Group = "Ranges")]
        public Color RangeColor { get; set; }

        [Parameter("Opacity", DefaultValue = 50, MinValue = 0, MaxValue = 255, Group = "Ranges")]
        public int Opacity { get; set; }

        [Parameter("Swing strength (bars)", DefaultValue = 5, MinValue = 2, Group = "Liquidity")]
        public int SwingStrength { get; set; }

        [Parameter("Equal tolerance (ATR)", DefaultValue = 0.1, MinValue = 0.01, Group = "Liquidity")]
        public double EqualTolerance { get; set; }

        [Parameter("Confirmation candle", DefaultValue = 3, MinValue = 1, MaxValue = 10, Group = "Liquidity")]
        public int ConfirmCandle { get; set; }

        [Parameter("Show failed sweeps", DefaultValue = false, Group = "Liquidity")]
        public bool ShowFailed { get; set; }

        [Parameter("Level color", DefaultValue = "Orange", Group = "Liquidity")]
        public Color LevelColor { get; set; }

        [Parameter("Bullish color", DefaultValue = "LimeGreen", Group = "Liquidity")]
        public Color BullColor { get; set; }

        [Parameter("Bearish color", DefaultValue = "Red", Group = "Liquidity")]
        public Color BearColor { get; set; }

        private class Range
        {
            public int Start, End;
            public double Top, Bottom;
        }

        private class Level
        {
            public string Label;
            public double Price;
            public bool IsHigh;
            public int From;        // where the level was formed
            public int ActiveFrom;  // first bar where it can be swept
        }

        private AverageTrueRange atr;
        private List<string> objects = new List<string>();
        private int lastIndex = -1;
        private int counter = 0;

        protected override void Initialize()
        {
            atr = Indicators.AverageTrueRange(14, MovingAverageType.Simple);
        }

        public override void Calculate(int index)
        {
            if (!IsLastBar || index == lastIndex)
                return;
            lastIndex = index;

            foreach (var name in objects)
                Chart.RemoveObject(name);
            objects.Clear();

            // only closed candles, the current one is still moving
            int last = index - 1;
            int first = Math.Max(20, last - BarsToScan);
            if (last - first < 50)
                return;

            var ranges = FindRanges(first, last);
            for (int r = Math.Max(0, ranges.Count - RangesToShow); r < ranges.Count; r++)
                DrawRange(ranges[r], last);

            var levels = FindEqualLevels(first, last);
            foreach (var range in ranges)
            {
                if (range.End >= last)
                    continue;
                levels.Add(new Level { Label = "range high", Price = range.Top, IsHigh = true, From = range.Start, ActiveFrom = range.End + 1 });
                levels.Add(new Level { Label = "range low", Price = range.Bottom, IsHigh = false, From = range.Start, ActiveFrom = range.End + 1 });
            }

            foreach (var level in levels)
                CheckSweep(level, last);
        }

        // ---------- ranges ----------

        private List<Range> FindRanges(int first, int last)
        {
            var list = new List<Range>();
            int i = first;
            while (i < last)
            {
                double top = Bars.HighPrices[i];
                double bottom = Bars.LowPrices[i];
                int j = i;

                // grow the range while it stays tight enough
                while (j + 1 <= last)
                {
                    double newTop = Math.Max(top, Bars.HighPrices[j + 1]);
                    double newBottom = Math.Min(bottom, Bars.LowPrices[j + 1]);
                    if (newTop - newBottom > MaxHeightAtr * atr.Result[j + 1])
                        break;
                    top = newTop;
                    bottom = newBottom;
                    j++;
                }

                if (j - i + 1 >= MinBars)
                {
                    list.Add(new Range { Start = i, End = j, Top = top, Bottom = bottom });
                    i = j + 1;
                }
                else
                {
                    i++;
                }
            }
            return list;
        }

        private void DrawRange(Range r, int last)
        {
            int end = Math.Min(r.End + 1, last + 1);
            var rect = Chart.DrawRectangle(NewName(), r.Start, r.Top, end, r.Bottom, Color.FromArgb(Opacity, RangeColor));
            rect.IsFilled = true;

            double pips = (r.Top - r.Bottom) / Symbol.PipSize;
            var text = Chart.DrawText(NewName(), "Range " + pips.ToString("F1"), r.Start, r.Top, RangeColor);
            text.FontSize = 8;
            text.VerticalAlignment = VerticalAlignment.Top;
        }

        // ---------- equal highs / lows ----------

        private bool IsSwingHigh(int i)
        {
            for (int k = 1; k <= SwingStrength; k++)
            {
                if (Bars.HighPrices[i - k] >= Bars.HighPrices[i] || Bars.HighPrices[i + k] > Bars.HighPrices[i])
                    return false;
            }
            return true;
        }

        private bool IsSwingLow(int i)
        {
            for (int k = 1; k <= SwingStrength; k++)
            {
                if (Bars.LowPrices[i - k] <= Bars.LowPrices[i] || Bars.LowPrices[i + k] < Bars.LowPrices[i])
                    return false;
            }
            return true;
        }

        private List<Level> FindEqualLevels(int first, int last)
        {
            var levels = new List<Level>();
            var highs = new List<int>();
            var lows = new List<int>();

            // a swing is only known SwingStrength bars later
            for (int i = first + SwingStrength; i <= last - SwingStrength; i++)
            {
                if (IsSwingHigh(i))
                {
                    var level = MatchEqual(highs, i, true);
                    if (level != null)
                        levels.Add(level);
                    highs.Add(i);
                }
                if (IsSwingLow(i))
                {
                    var level = MatchEqual(lows, i, false);
                    if (level != null)
                        levels.Add(level);
                    lows.Add(i);
                }
            }
            return levels;
        }

        // compare the new swing with the last few swings of the same type
        private Level MatchEqual(List<int> swings, int i, bool isHigh)
        {
            double price = isHigh ? Bars.HighPrices[i] : Bars.LowPrices[i];
            double tolerance = EqualTolerance * atr.Result[i];

            for (int s = swings.Count - 1; s >= 0 && s >= swings.Count - 5; s--)
            {
                int j = swings[s];
                double other = isHigh ? Bars.HighPrices[j] : Bars.LowPrices[j];
                if (Math.Abs(price - other) > tolerance)
                    continue;

                double levelPrice = isHigh ? Math.Max(price, other) : Math.Min(price, other);

                // price must not have gone through the level in between
                bool clean = true;
                for (int k = j + 1; k < i; k++)
                {
                    if ((isHigh && Bars.HighPrices[k] > levelPrice) || (!isHigh && Bars.LowPrices[k] < levelPrice))
                    {
                        clean = false;
                        break;
                    }
                }
                if (!clean)
                    continue;

                return new Level
                {
                    Label = isHigh ? "EQH" : "EQL",
                    Price = levelPrice,
                    IsHigh = isHigh,
                    From = j,
                    ActiveFrom = i + SwingStrength + 1
                };
            }
            return null;
        }

        // ---------- sweeps ----------

        private void CheckSweep(Level level, int last)
        {
            for (int k = level.ActiveFrom; k <= last; k++)
            {
                double high = Bars.HighPrices[k];
                double low = Bars.LowPrices[k];
                double close = Bars.ClosePrices[k];

                bool closedThrough = level.IsHigh ? close > level.Price : close < level.Price;
                if (closedThrough)
                    return;  // real breakout, not a sweep

                bool wickThrough = level.IsHigh ? high > level.Price : low < level.Price;
                if (wickThrough)
                {
                    DrawSweep(level, k, last);
                    return;
                }
            }

            // level still untouched
            if (level.Label == "EQH" || level.Label == "EQL")
                DrawLevel(level, last + 1);
        }

        private void DrawSweep(Level level, int sweepBar, int last)
        {
            int confirmBar = sweepBar + ConfirmCandle;
            string status;

            if (confirmBar > last)
            {
                status = "waiting " + (last - sweepBar) + "/" + ConfirmCandle;
            }
            else
            {
                bool ok = true;
                for (int j = sweepBar + 1; j <= confirmBar; j++)
                {
                    double c = Bars.ClosePrices[j];
                    if ((level.IsHigh && c > level.Price) || (!level.IsHigh && c < level.Price))
                        ok = false;
                }

                // the confirmation candle has to close further away than the sweep candle
                double sweepClose = Bars.ClosePrices[sweepBar];
                double confirmClose = Bars.ClosePrices[confirmBar];
                if (level.IsHigh && confirmClose >= sweepClose)
                    ok = false;
                if (!level.IsHigh && confirmClose <= sweepClose)
                    ok = false;

                if (!ok && !ShowFailed)
                    return;
                status = ok ? "confirmed" : "failed";
            }

            DrawLevel(level, sweepBar);

            // sweep of highs -> bearish, sweep of lows -> bullish
            Color color = level.IsHigh ? BearColor : BullColor;
            double y = level.IsHigh ? Bars.HighPrices[sweepBar] : Bars.LowPrices[sweepBar];
            string arrow = level.IsHigh ? "▼" : "▲";

            var text = Chart.DrawText(NewName(), arrow + " sweep " + level.Label + " (" + status + ")", sweepBar, y, color);
            text.FontSize = 8;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = level.IsHigh ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        }

        private void DrawLevel(Level level, int to)
        {
            Chart.DrawTrendLine(NewName(), level.From, level.Price, to, level.Price, LevelColor, 1, LineStyle.Lines);
            if (level.Label == "EQH" || level.Label == "EQL")
            {
                var text = Chart.DrawText(NewName(), level.Label, level.From, level.Price, LevelColor);
                text.FontSize = 8;
                text.HorizontalAlignment = HorizontalAlignment.Right;
            }
        }

        private string NewName()
        {
            string name = "rangezone_" + counter++;
            objects.Add(name);
            return name;
        }
    }
}
