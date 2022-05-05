# EggIncAPI

This is an example of how to query the status of an [Egg, Inc](https://egg-inc.fandom.com/wiki/Egg,_Inc.) co-op using the API by fanaticscripter: https://github.com/fanaticscripter/Egg

## Usage

There are both Python & C# examples for this API (note that not all examples exist for each language - they only exist to the extent that I've experimented with and decided to publish them).

# Python:

Clone the repo, cd to the `python` folder, and run `python getCoopStatus.py` (you may need to `pip install` a couple things like `protobuf`). It will spit out the current co-op status details as json. Modify for your needs.

# C#

*Note that this is built on .NET 6 so it may look different than C# you're used to as .NET 6 has implicit usings, namespace, & Main*

Clone the repo, cd to the `csharp` folder, and run `dotnet run`. Modify `Program.cs` for your needs.
