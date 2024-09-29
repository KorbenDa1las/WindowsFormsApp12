using GMap.NET;
using GMap.NET.WindowsForms.Markers;
using GMap.NET.WindowsForms;
using GMap.NET.MapProviders;
using System;
using System.IO;
using System.Windows.Forms;
using System.Drawing;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Globalization;

namespace WindowsFormsApp1
{
    public partial class Form1 : Form
    {
        private GMapOverlay markersOverlay;
        private GMapOverlay routesOverlay;
        private GMapRoute lastRoute;
        private GMapRoute drawnRoute;
        private PointLatLng firstPoint, secondPoint;
        private List<PointLatLng> drawnPoints = new List<PointLatLng>();
        private bool isFirstPointSelected = false;

        public Form1()
        {
            InitializeComponent();
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            string fileName = "Водні_перешкоди.txt";
            string filePath = Path.Combine(documentsPath, fileName);

            gMapControl1.MapProvider = GMapProviders.OpenStreetMap;
            gMapControl1.Position = new PointLatLng(49.841952, 24.031592);
            gMapControl1.MinZoom = 5;
            gMapControl1.MaxZoom = 100;
            gMapControl1.Zoom = 10;
            gMapControl1.Manager.Mode = AccessMode.ServerAndCache;
            gMapControl1.ShowCenter = false;

            markersOverlay = new GMapOverlay("markers");
            routesOverlay = new GMapOverlay("routes");

            gMapControl1.Overlays.Add(markersOverlay);
            gMapControl1.Overlays.Add(routesOverlay);

            gMapControl1.MouseClick += (s, mouseEvent) => gMapControl1_MouseClick(s, mouseEvent, filePath);
            this.KeyDown += Form1_KeyDown;
            this.KeyPreview = true;

        }
        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                // Запускаємо перевірку на перетин з річками після натискання Esc
                CheckForWaterIntersections(drawnPoints);

                // Після завершення перевірки дозволяємо почати нову лінію
                isFirstPointSelected = false;
            }
        }
        private void gMapControl1_MouseClick(object sender, MouseEventArgs e, string filePath)
        {
            if (e.Button == MouseButtons.Left)
            {
                var latLng = gMapControl1.FromLocalToLatLng(e.X, e.Y);

                // Якщо це перша точка нової лінії, очищуємо попередні маркери та лінії
                if (!isFirstPointSelected)
                {
                    // Очищуємо попередні маркери
                    markersOverlay.Clear();

                    // Очищуємо попередні маршрути (лінії), тільки якщо є щось для видалення
                    if (drawnRoute != null && routesOverlay.Routes.Contains(drawnRoute))
                    {
                        routesOverlay.Routes.Remove(drawnRoute);
                    }

                    // Очищуємо список точок для нової лінії
                    drawnPoints.Clear();

                    isFirstPointSelected = true; // Вказуємо, що вибрано першу точку нової лінії
                }

                // Додаємо маркер на карту
                GMapMarker marker = new GMarkerGoogle(latLng, GMarkerGoogleType.red_dot);
                markersOverlay.Markers.Add(marker);

                // Додаємо точку до списку
                drawnPoints.Add(latLng);

                // Якщо вже є ламана лінія, видаляємо її
                if (drawnRoute != null && routesOverlay.Routes.Contains(drawnRoute))
                {
                    routesOverlay.Routes.Remove(drawnRoute);
                }

                // Створюємо нову ламану лінію
                drawnRoute = new GMapRoute(drawnPoints, "drawnRoute")
                {
                    Stroke = new Pen(Color.Blue, 3)
                };
                routesOverlay.Routes.Add(drawnRoute);

                gMapControl1.Refresh();
            }
        }
        private async Task<List<(List<PointLatLng> points, string type)>> GetFeaturesInArea(PointLatLng topLeft, PointLatLng bottomRight)
        {
            string overpassUrl = "https://overpass-api.de/api/interpreter";
            string query = $@"
[out:json];
(
  way[""waterway""=""river""]({bottomRight.Lat.ToString("G", CultureInfo.InvariantCulture)},{topLeft.Lng.ToString("G", CultureInfo.InvariantCulture)},{topLeft.Lat.ToString("G", CultureInfo.InvariantCulture)},{bottomRight.Lng.ToString("G", CultureInfo.InvariantCulture)});
  way[""natural""=""wetland""]({bottomRight.Lat.ToString("G", CultureInfo.InvariantCulture)},{topLeft.Lng.ToString("G", CultureInfo.InvariantCulture)},{topLeft.Lat.ToString("G", CultureInfo.InvariantCulture)},{bottomRight.Lng.ToString("G", CultureInfo.InvariantCulture)});
  way[""wetland""]({bottomRight.Lat.ToString("G", CultureInfo.InvariantCulture)},{topLeft.Lng.ToString("G", CultureInfo.InvariantCulture)},{topLeft.Lat.ToString("G", CultureInfo.InvariantCulture)},{bottomRight.Lng.ToString("G", CultureInfo.InvariantCulture)});
);
out geom;
";


            using (HttpClient client = new HttpClient())
            {
                var response = await client.PostAsync(overpassUrl, new StringContent(query));
                string jsonResponse = await response.Content.ReadAsStringAsync();

                if (jsonResponse.StartsWith("<"))
                {
                    MessageBox.Show($"Відповідь сервера: {jsonResponse.Substring(0, 500)}");
                    return new List<(List<PointLatLng>, string)>();
                }

                if (string.IsNullOrWhiteSpace(jsonResponse))
                {
                    MessageBox.Show("Отримано пусту відповідь від сервера.");
                    return new List<(List<PointLatLng>, string)>();
                }
                
                var features = ParseFeaturesFromJson(jsonResponse);
                return features;
            }
        }
        private List<(List<PointLatLng> points, string type)> ParseFeaturesFromJson(string jsonResponse)
        {
            var features = new List<(List<PointLatLng>, string)>();
            try
            {
                var json = JObject.Parse(jsonResponse);
                foreach (var element in json["elements"])
                {
                    var featurePoints = new List<PointLatLng>();
                    var coordinates = element["geometry"] as JArray; // Витягуємо масив координат

                    string type = null;

                    if (element["tags"]["waterway"] != null)
                    {
                        type = "river";
                    }
                    else if (element["tags"]["natural"] != null && element["tags"]["natural"].ToString() == "wetland")
                    {
                        type = "wetland";
                    }
                    else if (element["tags"]["wetland"] != null)
                    {
                        type = element["tags"]["wetland"].ToString();
                    }
                    else
                    {
                        continue; // Пропускаємо, якщо тип не знайдено
                    }

                    if (coordinates != null)
                    {
                        foreach (var coordinate in coordinates)
                        {
                            double lat = (double)coordinate["lat"];
                            double lng = (double)coordinate["lon"];
                            featurePoints.Add(new PointLatLng(lat, lng));
                        }
                    }

                    features.Add((featurePoints, type));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка при парсингу JSON: {ex.Message}");
            }

            return features;
        }
        private bool AreLinesIntersecting(PointLatLng line1Start, PointLatLng line1End, PointLatLng line2Start, PointLatLng line2End, out PointLatLng intersection)
        {
            intersection = new PointLatLng();

            double A1 = line1End.Lat - line1Start.Lat;
            double B1 = line1Start.Lng - line1End.Lng;
            double C1 = A1 * line1Start.Lng + B1 * line1Start.Lat;

            double A2 = line2End.Lat - line2Start.Lat;
            double B2 = line2Start.Lng - line2End.Lng;
            double C2 = A2 * line2Start.Lng + B2 * line2Start.Lat;

            double det = A1 * B2 - A2 * B1;

            if (det == 0)
            {
                // Лінії паралельні
                return false;
            }
            else
            {
                double x = (B2 * C1 - B1 * C2) / det;
                double y = (A1 * C2 - A2 * C1) / det;

                intersection = new PointLatLng(y, x);

                // Перевірка, чи знаходиться точка перетину на обох відрізках
                if (IsPointOnLineSegment(intersection, line1Start, line1End) &&
                    IsPointOnLineSegment(intersection, line2Start, line2End))
                {
                    return true;
                }

                return false;
            }
        }
        private bool IsPointOnLineSegment(PointLatLng point, PointLatLng start, PointLatLng end)
        {
            double minX = Math.Min(start.Lng, end.Lng);
            double maxX = Math.Max(start.Lng, end.Lng);
            double minY = Math.Min(start.Lat, end.Lat);
            double maxY = Math.Max(start.Lat, end.Lat);

            return point.Lng >= minX && point.Lng <= maxX && point.Lat >= minY && point.Lat <= maxY;
        }
        private async void CheckForWaterIntersections(List<PointLatLng> points)
        {
            if (points.Count < 2)
            {
                MessageBox.Show("Лінія повинна мати принаймні дві точки.");
                return;
            }

            bool intersectionFound = false;
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string filePath = Path.Combine(documentsPath, "Водні_перешкоди.txt");

            // Перевірка кожного сегмента ламаної лінії
            for (int i = 0; i < points.Count - 1; i++)
            {
                PointLatLng start = points[i];
                PointLatLng end = points[i + 1];

                PointLatLng topLeft = new PointLatLng(Math.Max(start.Lat, end.Lat), Math.Min(start.Lng, end.Lng));
                PointLatLng bottomRight = new PointLatLng(Math.Min(start.Lat, end.Lat), Math.Max(start.Lng, end.Lng));

                var features = await GetFeaturesInArea(topLeft, bottomRight);

                foreach (var (featurePoints, type) in features)
                {
                    for (int j = 0; j < featurePoints.Count - 1; j++)
                    {
                        PointLatLng intersection;
                        if (AreLinesIntersecting(start, end, featurePoints[j], featurePoints[j + 1], out intersection))
                        {
                            intersectionFound = true;
                            string latDMS = ConvertToTrueForm(intersection.Lat, true);
                            string lngDMS = ConvertToTrueForm(intersection.Lng, false);

                            using (StreamWriter writer = new StreamWriter(filePath, true))
                            {
                                if (type == "river")
                                {
                                    writer.WriteLine($"Лінія перетинає річку в точці: {latDMS}, {lngDMS}");
                                }
                                else if (type == "wetland")
                                {
                                    writer.WriteLine($"Лінія перетинає болото в точці: {latDMS}, {lngDMS}");
                                }
                            }
                        }
                    }
                }
            }

            if (intersectionFound)
            {
                MessageBox.Show($"Координати перетину записано у файл: {filePath}");
            }
            else
            {
                MessageBox.Show("Перетин з водними перешкодами не знайдено.");
            }
        }
        private string ConvertToTrueForm(double decimalDegrees, bool isLatitude)
        {
            string direction = isLatitude ? (decimalDegrees >= 0 ? "N" : "S") : (decimalDegrees >= 0 ? "E" : "W");
            decimalDegrees = Math.Abs(decimalDegrees);
            int degrees = (int)decimalDegrees;
            double minutes = (decimalDegrees - degrees) * 60;
            int wholeMinutes = (int)minutes;
            double seconds = (minutes - wholeMinutes) * 60;

            string res = $"{Math.Abs(degrees)}°{wholeMinutes}'{seconds.ToString("F2", CultureInfo.InvariantCulture)}''{direction}";

            return res;
        }
    }
}