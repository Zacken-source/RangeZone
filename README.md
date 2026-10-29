# RangeZone

cTrader indicator for ranges and liquidity sweeps.

- ranges: zones where price stays within X * ATR for at least N bars
- liquidity: equal highs / equal lows and the edges of old ranges
- sweep: a candle wicks through a level but closes back inside
- the sweep is confirmed if the next 3 candles (configurable) don't close back through the level and the 3rd one closes further away than the sweep candle

## Install

In cTrader: Algo > Indicators > New, name it `RangeZone`, paste `RangeZone/RangeZone.cs` and build.
