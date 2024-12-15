using FFXIVLooseTextureCompiler.ImageProcessing;
using SamplePlugin;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace AQuestReborn.UIAtlasing
{
    public class UiAtlasManager
    {
        private Plugin _plugin;
        private List<byte[]> _icons;
        private byte[] _seperatorSection;
        private byte[] _rewardSection;
        private byte[] _biggerBoxSection;
        private byte[] _topPiece;
        private byte[] _bottomPiece;
        private byte[] _sidePiece;
        private byte[] _sidePieceRepeated;
        private byte[] _topCornerPiece;
        private byte[] _bottomCornerPiece;
        private byte[] _topCenterPiece;
        public List<byte[]> Icons { get => _icons; set => _icons = value; }
        public byte[] SeperatorSection { get => _seperatorSection; set => _seperatorSection = value; }
        public byte[] RewardSection { get => _rewardSection; set => _rewardSection = value; }
        public byte[] BiggerBoxSection { get => _biggerBoxSection; set => _biggerBoxSection = value; }
        public byte[] TopCenterPiece { get => _topCenterPiece; set => _topCenterPiece = value; }
        public byte[] TopPiece { get => _topPiece; set => _topPiece = value; }
        public byte[] BottomPiece { get => _bottomPiece; set => _bottomPiece = value; }
        public byte[] SidePiece { get => _sidePiece; set => _sidePiece = value; }
        public byte[] SidePieceRepeated { get => _sidePieceRepeated; set => _sidePieceRepeated = value; }
        public byte[] TopCornerPiece { get => _topCornerPiece; set => _topCornerPiece = value; }
        public byte[] BottomCornerPiece { get => _bottomCornerPiece; set => _bottomCornerPiece = value; }
        public UiAtlasManager(Plugin plugin)
        {
            _plugin = plugin;
            LoadJournalAssets();
        }
        void LoadJournalAssets()
        {
            var journalDetail = TexIO.TexToBitmap(new MemoryStream(_plugin.DataManager.GetFile("ui/uld/journal_detail.tex").Data));
            var journalFrame = TexIO.TexToBitmap(new MemoryStream(_plugin.DataManager.GetFile("ui/uld/journal_frame.tex").Data));
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
            _seperatorSection = memoryStream.ToArray();

            memoryStream = new MemoryStream();
            var rewardSection = ImageManipulation.Crop(journalDetail, new Vector2(376, 51), new Vector2(0, 29));
            rewardSection.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _rewardSection = memoryStream.ToArray();

            memoryStream = new MemoryStream();
            var biggerBoxSection = ImageManipulation.Crop(journalDetail, new Vector2(376, 92), new Vector2(0, 80));
            biggerBoxSection.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _biggerBoxSection = memoryStream.ToArray();


            //-----------------------------------------------------------------------------------------------------
            memoryStream = new MemoryStream();
            var topCenterPiece = ImageManipulation.Crop(journalFrame, new Vector2(82, 30), new Vector2(63, 50));
            topCenterPiece.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _topCenterPiece = memoryStream.ToArray();

            memoryStream = new MemoryStream();
            var topPiece = ImageManipulation.Crop(journalFrame, new Vector2(45, 27), new Vector2(63, 86));
            topPiece.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _topPiece = memoryStream.ToArray();

            memoryStream = new MemoryStream();
            var bottomPiece = ImageManipulation.Crop(journalFrame, new Vector2(90, 23), new Vector2(63, 114));
            bottomPiece.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _bottomPiece = memoryStream.ToArray();

            memoryStream = new MemoryStream();
            var sidePiece = ImageManipulation.Crop(journalFrame, new Vector2(33, 98), new Vector2(205, 7));
            sidePiece.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _sidePiece = memoryStream.ToArray();

            memoryStream = new MemoryStream();
            var sidePieceRepeated = ImageManipulation.Crop(journalFrame, new Vector2(23, 42), new Vector2(172, 53));
            sidePieceRepeated.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _sidePieceRepeated = memoryStream.ToArray();

            ImageManipulation.EraseSection(journalFrame, new Vector2(62, 50), new Vector2(115, 94));

            memoryStream = new MemoryStream();
            var topCornerPiece = ImageManipulation.Crop(journalFrame, new Vector2(156, 83), new Vector2(5, 6));
            topCornerPiece.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _topCornerPiece = memoryStream.ToArray();

            memoryStream = new MemoryStream();
            var bottomCornerPiece = ImageManipulation.Crop(journalFrame, new Vector2(156, 90), new Vector2(5, 95));
            bottomCornerPiece.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;
            _bottomCornerPiece = memoryStream.ToArray();
        }
    }
}
