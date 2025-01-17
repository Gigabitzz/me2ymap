﻿using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using SlimDX;

namespace YMapExporter
{
    public partial class YMapExporter : Form
    {
        private const float CarGenScale = 1.5f;
        private YMap _currentYMap = new YMap();

        public YMapExporter(string _cmdArg)
        {
            InitializeComponent();
            if (_cmdArg != "")
            {
                OpenFile(_cmdArg);
            }
        }

        private void YMapExporter_Load(object sender, EventArgs e)
        {
            RefreshView();
        }

        private void OpenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var o = new OpenFileDialog
            {
                Filter =
                    @"Map Editor files (*.xml)|*.xml|Menyoo Spooner files (*.xml)|*.xml|YMap files (*.ymap.xml)|*.ymap.xml"
            };

            if (o.ShowDialog() != DialogResult.OK)
                return;

            OpenFile(o.FileName);
        }


        private void OpenFile(string _filePath)
        {
            if (_filePath.EndsWith(".ymap.xml"))
            {
                using (var reader = XmlReader.Create(_filePath))
                {
                    var s = new XmlSerializer(typeof(YMap));
                    _currentYMap = (YMap)s.Deserialize(reader);
                    reader.Close();
                    RefreshView();
                    Text = @"ME2YM - " + Path.GetFileName(_filePath);
                }
            }
            else if (_filePath.EndsWith(".xml"))
            {
                MapEditorMap map = null;
                SpoonerPlacements spoonerMap = null;

                var reader = XmlReader.Create(_filePath);
                try
                {
                    var s = new XmlSerializer(typeof(MapEditorMap));
                    map = (MapEditorMap)s.Deserialize(reader);
                }
                catch
                {
                    try
                    {
                        var s = new XmlSerializer(typeof(SpoonerPlacements));
                        spoonerMap = (SpoonerPlacements)s.Deserialize(reader);
                        Text = @"ME2YM - " + Path.GetFileName(_filePath);
                    }
                    catch { /* ignored */ }
                }
                reader.Close();

                if (map == null && spoonerMap == null)
                {
                    MessageBox.Show(@"Failed to read file.");
                    return;
                }

                if (map != null) ConvertToYMap(map);
                else ConvertToYMap(spoonerMap);
            }
            else
            {
                MessageBox.Show(@"This is not a valid type!");
            }
        }


        private void ConvertToYMap(MapEditorMap map)
        {
            _currentYMap = new YMap();

            foreach (var mapObject in map.Objects)
            {
                var model = HashMap.GetModelName(mapObject.Hash);
                if (string.IsNullOrEmpty(model)) model = "0x" + mapObject.Hash.ToString("x");

                var position = mapObject.Position;
                switch (mapObject.Type)
                {
                    case MapObjectTypes.Vehicle:
                        CreateCarGenerator(model, position, mapObject.Quaternion, CarGenScale);
                        break;
                    case MapObjectTypes.Prop:
                        CreateEntity(model, position, mapObject.Quaternion, mapObject.Dynamic || mapObject.Door);
                        break;
                }
            }

            CalcExtents();
            RefreshView();
        }

        private void ConvertToYMap(SpoonerPlacements spoonerPlacements)
        {
            _currentYMap = new YMap();

            foreach (var placement in spoonerPlacements.Placements)
            {
                var model = placement.ModelHash;
                if (string.IsNullOrEmpty(model)) model = placement.HashName;

                var position = placement.PositionRotation.GetPosition();
                var rotation = placement.PositionRotation.GetQuaternion();
                switch (placement.Type)
                {
                    case 2:
                        CreateCarGenerator(model, position, rotation, CarGenScale);
                        break;
                    case 3:
                        CreateEntity(model, position, rotation, placement.Dynamic, false);
                        break;
                }
            }

            CalcExtents();
            RefreshView();
        }

        private void CreateEntity(string model, Vector3 position,
            Quaternion rotation, bool dynamic, bool conjugateRotation = true)
        {
            if (conjugateRotation)
            {
                if (rotation.W < 0) rotation.W = -rotation.W;
                else rotation.Conjugate();
            }

            var ent = new Entity
            {
                Position = new XmlVector3(position.X, position.Y, position.Z),
                Rotation = new XmlQuaternion(rotation.X, rotation.Y, rotation.Z, rotation.W),
                ArchetypeName = model
            };

            if (!dynamic)
                ent.Flags = new XmlValue<int>(32);

            _currentYMap.Entities.Add(ent);
        }

        private void CreateCarGenerator(string model, Vector3 position,
            Quaternion rotation, float scale)
        {
            var v = new Vector3(0, scale, 0);
            var direction = rotation.Multiply(v);
            var orientX = direction.X;
            var orientY = direction.Y;

            var car = new CarGenerator
            {
                Position = new XmlVector3(position.X, position.Y, position.Z),
                OrientationX = new XmlValue<float>(orientX),
                OrientationY = new XmlValue<float>(orientY),
                PerpendicularLength = new XmlValue<float>(scale),
                CarModel = model
            };

            _currentYMap.CarGenerators.Add(car);
        }

        private void ExportToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var s = new SaveFileDialog
            {
                Filter = @"YMap files (*.ymap.xml)|*.ymap.xml"
            };

            if (s.ShowDialog() != DialogResult.OK)
                return;

            using (var w = XmlWriter.Create(s.FileName,
                new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(),
                    Indent = true
                }))
            {
                var ns = new XmlSerializerNamespaces();
                ns.Add("", "");
                var ser = new XmlSerializer(typeof(YMap));
                w.WriteStartDocument(false);
                ser.Serialize(w, _currentYMap, ns);
            }
        }

        private void QuitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void NewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _currentYMap = new YMap();
            Text = @"ME2YM";
            RefreshView();
        }

        private void CalculateExtentsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CalcExtents();
        }

        private void CalcExtents()
        {
            if (!_currentYMap.Entities.Any() && !_currentYMap.CarGenerators.Any())
                return;

            var centre = new Vector3();

            centre = _currentYMap.Entities.Aggregate(centre,
                (current, entity) => current + new Vector3(entity.Position.X, entity.Position.Y, entity.Position.Z));
            centre = _currentYMap.CarGenerators.Aggregate(centre,
                (current, carGenerator) => current + new Vector3(carGenerator.Position.X, carGenerator.Position.Y,
                                               carGenerator.Position.Z));

            centre /= _currentYMap.Entities.Count + _currentYMap.CarGenerators.Count;

            _currentYMap.StreamingExtenstsMin = new XmlVector3(centre.X - 10000,
                centre.Y - 10000, centre.Z - 1000);
            _currentYMap.StreamingExtenstsMax = new XmlVector3(centre.X + 10000,
                centre.Y + 10000, centre.Z + 5000);
            _currentYMap.EntitiesExtenstsMin = new XmlVector3(centre.X - 10000,
                centre.Y - 10000, centre.Z - 1000);
            _currentYMap.EntitiesExtenstsMax = new XmlVector3(centre.X + 10000,
                centre.Y + 10000, centre.Z + 5000);

            RefreshView();
        }

        private void RefreshView()
        {
            propertyGrid1.SelectedObject = _currentYMap;
        }
    }
}