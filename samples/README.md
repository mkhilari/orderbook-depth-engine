# Sample data

This folder holds the task's provided sample so you can run the app immediately
after building — every command in the top-level [README](../README.md) points here.

| File | What it is |
|------|------------|
| `input1.stream` | The short binary order-book feed provided with the task. |
| `output1.log`   | The expected output for `input1.stream` at a depth of 5 levels. The app reproduces this byte-for-byte. |

Quick check that your build is correct (run from the repository root):

```bash
dotnet run --project src/OrderBookFeed.App -c Release -- 5 samples/input1.stream | diff - samples/output1.log && echo "OK: output matches the provided sample"
```

> The larger `input2.stream` referenced in the task is **not** bundled here — point the
> app at your own copy of it (or any feed file) to generate `output2.log`. The automated
> test suite also uses a separate synthetic fixture under
> `tests/OrderBookFeed.Tests/Samples/`.
