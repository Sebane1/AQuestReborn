using FFXIVClientStructs.FFXIV.Client.Game.Character; class Dump { unsafe void Run(Character* c) { c->GameObject.RenderFlags = 0; } }
