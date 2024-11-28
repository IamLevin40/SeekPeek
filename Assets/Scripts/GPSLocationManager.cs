using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using Unity.Notifications.Android;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class GPSLocationManager : MonoBehaviour
{
    public string thingSpeakChannelId = "2729123";
    public string thingSpeakReadAPIKey = "FI6RJNDMMI3R2PX8";
    public string mapboxAccessToken = "pk.eyJ1IjoiaWFtbGV2aW4iLCJhIjoiY20zNTVneHZxMDE0NDJuc2FuazBsMXNqcCJ9.ufMmWdL6ivNgDSKCMxb5iw";
    private double latitude;
    private double longitude;
    private int zoomLevel = 15;
    private int mapWidth = 1000;
    private int mapHeight = 1000;

    public Text latitudeText;
    public Text longitudeText;
    public RawImage mapImage;
    public Toggle locationToggle; // Reference to the toggle UI
    private bool isToggleOn = false;

    private const string POST_NOTIFICATIONS_PERMISSION = "android.permission.POST_NOTIFICATIONS";


    private void Start()
    {
        InitializeNotifications();
        RequestNotificationPermission();
        locationToggle.onValueChanged.AddListener(OnToggleChanged);
        StartCoroutine(GetGPSData());
        StartForegroundService();
    }

    private void OnToggleChanged(bool value)
    {
        isToggleOn = value;
        Debug.Log($"Toggle changed: {isToggleOn}");

        if (isToggleOn)
            SaveLocationToFile(latitude, longitude);
    }

    private IEnumerator GetGPSData()
    {
        while (true)
        {
            // Fetch GPS data from ThingSpeak
            string url = $"https://api.thingspeak.com/channels/{thingSpeakChannelId}/feeds.json?api_key={thingSpeakReadAPIKey}&results=1";
            Debug.Log($"URL: {url}");
            UnityWebRequest www = UnityWebRequest.Get(url);
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error fetching GPS data: " + www.error);
            }
            else
            {
                string jsonText = www.downloadHandler.text;
                ThingSpeakData data = JsonUtility.FromJson<ThingSpeakData>(jsonText);
                latitude = double.Parse(data.channel.latitude, System.Globalization.CultureInfo.InvariantCulture);
                longitude = double.Parse(data.channel.longitude, System.Globalization.CultureInfo.InvariantCulture);

                latitudeText.text = $"{latitude.ToString("f6")}°";
                longitudeText.text = $"{longitude.ToString("f6")}°";

                // Save location if toggle is on
                if (isToggleOn)
                {
                    Vector2d? savedLocation = LoadLocationFromFile();
                    if (savedLocation.HasValue)
                    {
                        double distance = HaversineDistance(savedLocation.Value.x, savedLocation.Value.y, latitude, longitude);
                        Debug.Log($"Saved: {savedLocation.Value.x}, {savedLocation.Value.y}... Current: {latitude}, {longitude}");
                        if (distance > 0.000000001) // Use an appropriate threshold for the distance
                        {
                            SendNotification("Warning", "Item is being moved by unknown!");
                        }
                    }
                }
            }

            StartCoroutine(GetMapImage());
            yield return new WaitForSeconds(7.5f);
        }
    }

    private double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371e3; // Radius of Earth in meters
        double phi1 = lat1 * Mathf.Deg2Rad;
        double phi2 = lat2 * Mathf.Deg2Rad;
        double deltaPhi = (lat2 - lat1) * Mathf.Deg2Rad;
        double deltaLambda = (lon2 - lon1) * Mathf.Deg2Rad;

        double a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) *
                Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c; // Distance in meters
    }

    private IEnumerator GetMapImage()
    {
        // Construct the Mapbox URL with the current latitude and longitude
        string url = $"https://api.mapbox.com/styles/v1/mapbox/streets-v11/static/pin-l-marker+ff0000({longitude},{latitude})/{longitude},{latitude},{zoomLevel},0/{mapWidth}x{mapHeight}?access_token={mapboxAccessToken}";
        Debug.Log(url);
        UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError("Error fetching map: " + request.error);
        }
        else
        {
            // Apply the downloaded texture to the RawImage component
            Texture2D mapTexture = ((DownloadHandlerTexture)request.downloadHandler).texture;
            mapImage.texture = mapTexture;
            mapImage.color = new Color(1, 1, 1, 1);
        }
    }

    private void SaveLocationToFile(double latitude, double longitude)
    {
        string filePath = Application.persistentDataPath + "/location.dat";
        string data = $"{latitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}";
        System.IO.File.WriteAllText(filePath, data);
        Debug.Log($"Saved location: {data}");
    }

    private Vector2d? LoadLocationFromFile()
    {
        string filePath = Application.persistentDataPath + "/location.dat";
        if (System.IO.File.Exists(filePath))
        {
            string data = System.IO.File.ReadAllText(filePath);
            string[] parts = data.Split(',');
            if (parts.Length == 2 &&
                double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double savedLatitude) &&
                double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double savedLongitude))
            {
                Debug.Log($"Loaded: {savedLatitude}, {savedLongitude}");
                return new Vector2d(savedLatitude, savedLongitude);
            }
        }
        return null;
    }

    private void InitializeNotifications()
    {
        var channel = new AndroidNotificationChannel()
        {
            Id = "default_channel",
            Name = "Default Channel",
            Importance = Importance.High,
            Description = "Notification channel for GPS updates",
        };
        AndroidNotificationCenter.RegisterNotificationChannel(channel);
    }
    
    private void RequestNotificationPermission()
    {
        if (Permission.HasUserAuthorizedPermission(POST_NOTIFICATIONS_PERMISSION))
        {
            Debug.Log("Notification permission already granted.");
        }
        else
        {
            Permission.RequestUserPermission(POST_NOTIFICATIONS_PERMISSION);
        }
    }

    private void SendNotification(string title, string text)
    {
        Debug.Log($"{title}. {text}");
        var notification = new AndroidNotification()
        {
            Title = title,
            Text = text,
            FireTime = System.DateTime.Now
        };
        AndroidNotificationCenter.SendNotification(notification, "default_channel");
    }

    private void StartForegroundService()
    {
    #if UNITY_ANDROID
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var intent = new AndroidJavaObject("android.content.Intent", 
                currentActivity, 
                new AndroidJavaClass("com.yourcompany.yourappname.LocationForegroundService")))
            {
                currentActivity.Call("startService", intent);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to start foreground service: {ex.Message}");
        }
    #endif
    }


    [System.Serializable]
    public class ChannelData
    {
        public int id;
        public string name;
        public string latitude;
        public string longitude;
    }

    [System.Serializable]
    public class ThingSpeakData
    {
        public ChannelData channel;
    }
}

public struct Vector2d
{
    public double x;
    public double y;

    public Vector2d(double x, double y)
    {
        this.x = x;
        this.y = y;
    }
}
