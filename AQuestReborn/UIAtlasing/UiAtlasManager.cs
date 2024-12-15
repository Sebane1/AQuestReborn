using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Common.Lua;
using FFXIVLooseTextureCompiler.ImageProcessing;
using ImGuiNET;
using SamplePlugin;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using Color = System.Drawing.Color;

namespace AQuestReborn.UIAtlasing
{
    public class UiAtlasManager
    {
        private List<byte[]> _icons;
        private byte[] _seperatorSectionBytes;
        private byte[] _rewardSectionBytes;
        private byte[] _biggerBoxSectionBytes;
        private byte[] _topPieceBytes;
        private byte[] _bottomPieceBytes;
        private byte[] _sidePieceBytes;
        private byte[] _sidePieceRepeatedBytes;
        private byte[] _topCornerPieceBytes;
        private byte[] _bottomCornerPieceBytes;
        private byte[] _topCenterPieceBytes;
        private float _globalScale;
        private IDalamudTextureWrap _seperatorSection;
        private IDalamudTextureWrap _rewardSection;
        private IDalamudTextureWrap _biggerBoxSection;
        private IDalamudTextureWrap _topPiece;
        private IDalamudTextureWrap _bottomPiece;
        private IDalamudTextureWrap _sidePiece;
        private IDalamudTextureWrap _sidePieceRepeated;
        private IDalamudTextureWrap _topCornerPiece;
        private IDalamudTextureWrap _bottomCornerPiece;
        private IDalamudTextureWrap _topCenterPiece;
        private IDalamudTextureWrap _reward;
        private IDalamudTextureWrap _description;
        private IDalamudTextureWrap _rating;
        private IDalamudTextureWrap _backgroundImage;
        private byte[] _backgroundFill;
        private bool _alreadyLoadingData;

        public List<byte[]> Icons { get => _icons; set => _icons = value; }
        public byte[] SeperatorSection { get => _seperatorSectionBytes; set => _seperatorSectionBytes = value; }
        public byte[] RewardSection { get => _rewardSectionBytes; set => _rewardSectionBytes = value; }
        public byte[] BiggerBoxSection { get => _biggerBoxSectionBytes; set => _biggerBoxSectionBytes = value; }
        public byte[] TopCenterPiece { get => _topCenterPieceBytes; set => _topCenterPieceBytes = value; }
        public byte[] TopPiece { get => _topPieceBytes; set => _topPieceBytes = value; }
        public byte[] BottomPiece { get => _bottomPieceBytes; set => _bottomPieceBytes = value; }
        public byte[] SidePiece { get => _sidePieceBytes; set => _sidePieceBytes = value; }
        public byte[] SidePieceRepeated { get => _sidePieceRepeatedBytes; set => _sidePieceRepeatedBytes = value; }
        public byte[] TopCornerPiece { get => _topCornerPieceBytes; set => _topCornerPieceBytes = value; }
        public byte[] BottomCornerPiece { get => _bottomCornerPieceBytes; set => _bottomCornerPieceBytes = value; }
        public Plugin Plugin { get; private set; }

        public UiAtlasManager(Plugin plugin)
        {
            Plugin = plugin;

            LoadJournalAssets();

            Bitmap background = new Bitmap(1, 1);
            Graphics graphics = Graphics.FromImage(background);
            graphics.Clear(Color.FromArgb(255, 232, 225, 216));
            graphics.Save();
            MemoryStream memoryStream = new MemoryStream();
            background.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _backgroundFill = memoryStream.ToArray();
        }
        public void TextCentered(string text, Vector2 size)
        {
            var windowWidth = size.X;
            var fontSize = 1;
            ImGui.SetWindowFontScale(1.8f);
            var textWidth = ImGui.CalcTextSize(text).X;
            ImGui.SetCursorPosX((windowWidth - textWidth) * 0.5f);
            ImGui.Text(text);
        }
        void LoadJournalAssets()
        {
            var journalDetail = TexIO.TexToBitmap(new MemoryStream(Plugin.DataManager.GetFile("ui/uld/journal_detail.tex").Data));
            var journalFrame = TexIO.TexToBitmap(new MemoryStream(Plugin.DataManager.GetFile("ui/uld/journal_frame.tex").Data));
            var iconSection = ImageManipulation.Crop(journalDetail, new Vector2(264, 24));

            MemoryStream memoryStream;
            var icons = ImageManipulation.DivideImageHorizontally(iconSection, 11);
            _icons = new List<byte[]>();
            foreach (var icon in icons)
            {
                memoryStream = new MemoryStream();
                icon.Save(memoryStream, ImageFormat.Png);
                memoryStream.Position = 0;
                _icons.Add(memoryStream.ToArray());
            }

            //-----------------------------------------------------------------------------------------------------
            memoryStream = new MemoryStream();
            var seperatorSection = ImageManipulation.Crop(journalDetail, new Vector2(390, 2), new Vector2(1, 25));
            seperatorSection.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _seperatorSectionBytes = memoryStream.ToArray();

            memoryStream = new MemoryStream();
            var rewardSection = ImageManipulation.Crop(journalDetail, new Vector2(376, 51), new Vector2(0, 29));
            rewardSection.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _rewardSectionBytes = memoryStream.ToArray();

            memoryStream = new MemoryStream();
            var biggerBoxSection = ImageManipulation.Crop(journalDetail, new Vector2(376, 92), new Vector2(0, 80));
            biggerBoxSection.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _biggerBoxSectionBytes = memoryStream.ToArray();


            //-----------------------------------------------------------------------------------------------------
            memoryStream = new MemoryStream();
            var topCenterPiece = ImageManipulation.Crop(journalFrame, new Vector2(82, 30), new Vector2(63, 50));
            topCenterPiece.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _topCenterPieceBytes = memoryStream.ToArray();

            memoryStream = new MemoryStream();
            var topPiece = ImageManipulation.Crop(journalFrame, new Vector2(45, 27), new Vector2(63, 86));
            topPiece.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _topPieceBytes = memoryStream.ToArray();

            memoryStream = new MemoryStream();
            var bottomPiece = ImageManipulation.Crop(journalFrame, new Vector2(90, 23), new Vector2(63, 114));
            bottomPiece.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _bottomPieceBytes = memoryStream.ToArray();

            memoryStream = new MemoryStream();
            var sidePiece = ImageManipulation.Crop(journalFrame, new Vector2(33, 98), new Vector2(205, 7));
            sidePiece.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _sidePieceBytes = memoryStream.ToArray();

            memoryStream = new MemoryStream();
            var sidePieceRepeated = ImageManipulation.Crop(journalFrame, new Vector2(23, 42), new Vector2(173, 53));
            sidePieceRepeated.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _sidePieceRepeatedBytes = memoryStream.ToArray();

            ImageManipulation.EraseSection(journalFrame, new Vector2(62, 50), new Vector2(115, 94));

            memoryStream = new MemoryStream();
            var topCornerPiece = ImageManipulation.Crop(journalFrame, new Vector2(156, 83), new Vector2(5, 6));
            topCornerPiece.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _topCornerPieceBytes = memoryStream.ToArray();

            memoryStream = new MemoryStream();
            var bottomCornerPiece = ImageManipulation.Crop(journalFrame, new Vector2(156, 90), new Vector2(5, 95));
            bottomCornerPiece.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _bottomCornerPieceBytes = memoryStream.ToArray();
        }
        public void DrawBackground(Vector2 size)
        {
            if (_backgroundImage != null)
            {
                var relativeSize = size * 0.98f;
                ImGui.SetCursorPos(new Vector2((size.X / 2) - (relativeSize.X / 2), 0));
                ImGui.Image(_backgroundImage.ImGuiHandle, relativeSize);
            }
        }
        public void DrawSeperator(float width)
        {
            if (_seperatorSection != null)
            {
                ImGui.Image(_seperatorSection.ImGuiHandle, new Vector2(width, _seperatorSection.Height));
            }
        }
        public void DrawRewardImage(Vector2 size)
        {
            ImGui.Image(_reward.ImGuiHandle, size);
        }
        public void DrawDescriptionImage(Vector2 size)
        {
            ImGui.Image(_description.ImGuiHandle, size);
        }
        public void DrawRatingImage(Vector2 size)
        {
            ImGui.Image(_rating.ImGuiHandle, size);
        }
        public void CheckImageAssets()
        {
            if (!_alreadyLoadingData)
            {
                Task.Run(async () =>
                {
                    _alreadyLoadingData = true;
                    if (_reward == null)
                    {
                        _reward = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.Icons[2]);
                    }
                    if (_description == null)
                    {
                        _description = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.Icons[3]);
                    }
                    if (_rating == null)
                    {
                        _rating = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.Icons[0]);
                    }
                    if (_seperatorSection == null)
                    {
                        _seperatorSection = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.SeperatorSection);
                    }
                    if (_rewardSection == null)
                    {
                        _rewardSection = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.RewardSection);
                    }
                    if (_biggerBoxSection == null)
                    {
                        _biggerBoxSection = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.BiggerBoxSection);
                    }
                    if (_topPiece == null)
                    {
                        _topPiece = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.TopPiece);
                    }
                    if (_bottomPiece == null)
                    {
                        _bottomPiece = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.BottomPiece);
                    }
                    if (_sidePiece == null)
                    {
                        _sidePiece = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.SidePiece);
                    }
                    if (_sidePieceRepeated == null)
                    {
                        _sidePieceRepeated = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.SidePieceRepeated);
                    }
                    if (_topCornerPiece == null)
                    {
                        _topCornerPiece = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.TopCornerPiece);
                    }
                    if (_bottomCornerPiece == null)
                    {
                        _bottomCornerPiece = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.BottomCornerPiece);
                    }
                    if (_backgroundImage == null)
                    {
                        _backgroundImage = await Plugin.TextureProvider.CreateFromImageAsync(_backgroundFill);
                    }
                    //if (_topCenterPiece == null)
                    //{
                    //    _topCenterPiece = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.TopCenterPiece);
                    //}
                    _alreadyLoadingData = false;
                });
            }
        }
        public void DrawFrameBorders(Vector2 size)
        {
            _globalScale = ImGuiHelpers.GlobalScale;
            //if (_rewardSection == null)
            //{
            //    _rewardSection = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.RewardSection);
            //}
            //if (_biggerBoxSection == null)
            //{
            //    _biggerBoxSection = await Plugin.TextureProvider.CreateFromImageAsync(Plugin.UiAtlasManager.BiggerBoxSection);
            //}
            if (_topCornerPiece != null && _bottomCornerPiece != null)
            {
                var relativeTopCornerScaling = _topCornerPiece.Size * _globalScale;
                var relativeBottomCornerScaling = _bottomCornerPiece.Size * _globalScale;
                if (_topCenterPiece != null)
                {
                    var relativeScaling = _topCenterPiece.Size * _globalScale;
                    ImGui.SetCursorPos(new Vector2((size.X / 2) - (relativeScaling.X / 2), 0));
                    ImGui.Image(_topCenterPiece.ImGuiHandle, relativeScaling);
                }
                if (_topPiece != null)
                {
                    var relativeScaling = _topPiece.Size * _globalScale;
                    var pieceSize = new Vector2(size.X - (relativeTopCornerScaling.X * 2), relativeScaling.Y);
                    ImGui.SetCursorPos(new Vector2((size.X / 2) - (pieceSize.X / 2), 0));
                    ImGui.Image(_topPiece.ImGuiHandle, pieceSize);
                }
                if (_sidePieceRepeated != null)
                {
                    var relativeScaling = _sidePieceRepeated.Size * _globalScale;
                    ImGui.SetCursorPos(new Vector2(0, 0));
                    ImGui.Image(_sidePieceRepeated.ImGuiHandle, new Vector2(relativeScaling.X, size.Y - (20 * _globalScale)));

                    ImGui.SetCursorPos(new Vector2(size.X - relativeScaling.X, 0));
                    ImGui.Image(_sidePieceRepeated.ImGuiHandle, new Vector2(relativeScaling.X, size.Y - (20 * _globalScale)), new Vector2(1, 0), new Vector2(0, 1));
                }
                if (_sidePiece != null)
                {
                    var relativeScaling = _sidePiece.Size * _globalScale;
                    ImGui.SetCursorPos(new Vector2(0, 83 * _globalScale));
                    ImGui.Image(_sidePiece.ImGuiHandle, relativeScaling);

                    ImGui.SetCursorPos(new Vector2(size.X - relativeScaling.X, 83 * _globalScale));
                    ImGui.Image(_sidePiece.ImGuiHandle, relativeScaling, new Vector2(1, 0), new Vector2(0, 1));
                }
                if (_bottomPiece != null)
                {
                    var relativeScaling = _bottomPiece.Size * _globalScale;
                    var pieceSize = new Vector2(size.X - (relativeBottomCornerScaling.X * 2), relativeScaling.Y);
                    ImGui.SetCursorPos(new Vector2((size.X / 2) - (pieceSize.X / 2), size.Y - relativeScaling.Y));
                    ImGui.Image(_bottomPiece.ImGuiHandle, pieceSize);
                }

                ImGui.SetCursorPos(new Vector2(0, 0));
                ImGui.Image(_topCornerPiece.ImGuiHandle, relativeTopCornerScaling);

                ImGui.SetCursorPos(new Vector2(size.X - relativeTopCornerScaling.X, 0));
                ImGui.Image(_topCornerPiece.ImGuiHandle, relativeTopCornerScaling, new Vector2(1, 0), new Vector2(0, 1));

                ImGui.SetCursorPos(new Vector2(0, size.Y - relativeBottomCornerScaling.Y));
                ImGui.Image(_bottomCornerPiece.ImGuiHandle, relativeBottomCornerScaling);

                ImGui.SetCursorPos(new Vector2(size.X - relativeBottomCornerScaling.X, size.Y - relativeBottomCornerScaling.Y));
                ImGui.Image(_bottomCornerPiece.ImGuiHandle, relativeBottomCornerScaling, new Vector2(1, 0), new Vector2(0, 1));
            }
        }
    }
}
