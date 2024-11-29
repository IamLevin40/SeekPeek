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
    private DateTime currentDateTime;
    private double currentLatitude;
    private double currentLongitude;
    private double currentAltitude;
    private int zoomLevel = 15;
    private int mapWidth = 1000;
    private int mapHeight = 1000;
    private bool isNotifyToggleOn = false;

    public Button informationTabButton;
    public Button settingsTabButton;
    public GameObject informationTab;
    public GameObject settingsTab;
    public Text currentDateTimeText;
    public Text currentLatitudeText;
    public Text currentLongitudeText;
    public Text currentAltitudeText;
    public Text savedDateTimeText;
    public Text savedLatitudeText;
    public Text savedLongitudeText;
    public Text savedAltitudeText;
    public RawImage mapImage;
    public Toggle notifyToggle;
    public Button saveLocationButton;
    public Slider thresholdSlider;
    public InputField thresholdInputField;

    private double threshold = 0.001;
    private double previousThreshold;

    private const string POST_NOTIFICATIONS_PERMISSION = "android.permission.POST_NOTIFICATIONS";


    private void Start()
    {
        OnTabPressed("information_tab");
        
        InitializeNotifications();
        RequestNotificationPermission();
        StartForegroundService();

        PrepareListeners();
        StartCoroutine(GetGPSData());
    }

    private void PrepareListeners()
    {
        notifyToggle.onValueChanged.AddListener(OnNotifyToggled);
        informationTabButton.onClick.AddListener(() => OnTabPressed("information_tab"));
        settingsTabButton.onClick.AddListener(() => OnTabPressed("settings_tab"));
        
        thresholdSlider.onValueChanged.AddListener(value => UpdateThresholdFromSlider(value));
        thresholdInputField.onEndEdit.AddListener(value => UpdateThresholdFromInput(value));
        previousThreshold = threshold;
        UpdateThresholdUI();

        Vector3d? savedLocation = LoadLocationFromFile();
        if (savedLocation.HasValue)
        {
            DisplaySavedLocation(savedLocation.Value.dateTime, savedLocation.Value.latitude, savedLocation.Value.longitude, savedLocation.Value.altitude);
        }
    }

    private void OnNotifyToggled(bool value)
    {
        isNotifyToggleOn = value;
        Debug.Log($"Notify toggle changed: {isNotifyToggleOn}");
    }

    private void OnTabPressed(string tab)
    {
        informationTab.SetActive(false);
        settingsTab.SetActive(false);

        switch (tab)
        {
            case "information_tab":
                informationTab.SetActive(true);
                break;
            case "settings_tab":
                settingsTab.SetActive(true);
                break;
        }
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
                
                if (data.feeds.Length > 0)
                {
                    currentDateTime = DateTime.Parse(data.feeds[0].created_at, null, System.Globalization.DateTimeStyles.RoundtripKind);
                    currentDateTime = currentDateTime.AddHours(-4);

                    currentLatitude = double.Parse(data.feeds[0].field1, System.Globalization.CultureInfo.InvariantCulture);
                    currentLongitude = double.Parse(data.feeds[0].field2, System.Globalization.CultureInfo.InvariantCulture);
                    currentAltitude = double.Parse(data.feeds[0].field3, System.Globalization.CultureInfo.InvariantCulture);
                    saveLocationButton.onClick.AddListener(() => SaveLocationToFile(currentDateTime, currentLatitude, currentLongitude, currentAltitude));

                    currentDateTimeText.text = FormatTimestamp(currentDateTime);
                    currentLatitudeText.text = $"{currentLatitude.ToString("f6")}°";
                    currentLongitudeText.text = $"{currentLongitude.ToString("f6")}°";
                    currentAltitudeText.text = $"{currentAltitude.ToString("f2")}m";

                    if (isNotifyToggleOn)
                    {
                        Vector3d? savedLocation = LoadLocationFromFile();
                        if (savedLocation.HasValue)
                        {
                            double distance = HaversineDistance(savedLocation.Value.latitude, savedLocation.Value.longitude, currentLatitude, currentLongitude);
                            Debug.Log($"Saved: {savedLocation.Value.latitude}, {savedLocation.Value.longitude}... Current: {currentLatitude}, {currentLongitude}");
                            if (distance > threshold)
                            {
                                SendNotification("Warning", "Item is being moved by unknown!");
                            }
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
        // Construct the Mapbox URL with the current currentLatitude and currentLongitude
        string url = $"https://api.mapbox.com/styles/v1/mapbox/streets-v11/static/pin-l-marker+ff0000({currentLongitude},{currentLatitude})/{currentLongitude},{currentLatitude},{zoomLevel},0/{mapWidth}x{mapHeight}?access_token={mapboxAccessToken}";
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

    private string FormatTimestamp(DateTime createdAt)
    {
        string formattedTimestamp = createdAt.ToString("yyyy-MM-dd | HH:mm:ss");
        Debug.Log($"Formatted Timestamp: {formattedTimestamp}");
        return formattedTimestamp;
    }

    private void DisplaySavedLocation(DateTime createdAt, double latitude, double longitude, double altitude)
    {
        savedDateTimeText.text = FormatTimestamp(createdAt);
        savedLatitudeText.text = $"{latitude.ToString("f6")}°";
        savedLongitudeText.text = $"{longitude.ToString("f6")}°";
        savedAltitudeText.text = $"{altitude.ToString("f2")}m";
    }

    private void SaveLocationToFile(DateTime createdAt, double latitude, double longitude, double altitude)
    {
        string filePath = Application.persistentDataPath + "/location.dat";
        string data = $"{createdAt},{latitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},{altitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}";
        System.IO.File.WriteAllText(filePath, data);
        
        DisplaySavedLocation(createdAt, latitude, longitude, altitude);
        Debug.Log($"Saved location: {data}");
    }

    private Vector3d? LoadLocationFromFile()
    {
        string filePath = Application.persistentDataPath + "/location.dat";
        if (System.IO.File.Exists(filePath))
        {
            string data = System.IO.File.ReadAllText(filePath);
            string[] parts = data.Split(',');

            if (parts.Length == 4 &&
                DateTime.TryParse(parts[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime savedDateTime) &&
                double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double savedLatitude) &&
                double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double savedLongitude) &&
                double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double savedAltitude))
            {
                Debug.Log($"Loaded: {savedDateTime}, {savedLatitude}, {savedLongitude}, {savedAltitude}");
                return new Vector3d(savedDateTime, savedLatitude, savedLongitude, savedAltitude);
            }
        }
        return null;
    }

    private void UpdateThresholdFromSlider(float sliderValue)
    {
        // Map slider value to appropriate range and increment
        if (sliderValue < 0.25f)
            threshold = 0.00001 + sliderValue / 0.25 * (0.0001 - 0.00001);
        else if (sliderValue < 0.5f)
            threshold = 0.0001 + (sliderValue - 0.25f) / 0.25 * (0.001 - 0.0001);
        else if (sliderValue < 0.75f)
            threshold = 0.001 + (sliderValue - 0.5f) / 0.25 * (0.01 - 0.001);
        else
            threshold = 0.01 + (sliderValue - 0.75f) / 0.25 * (0.1 - 0.01);

        UpdateThresholdInputField();
    }

    private void UpdateThresholdFromInput(string inputValue)
    {
        if (double.TryParse(inputValue, out double inputThreshold) && inputThreshold >= 0.00001 && inputThreshold <= 0.1)
        {
            threshold = inputThreshold;
            UpdateThresholdSlider();
            previousThreshold = threshold;
        }
        else
        {
            thresholdInputField.text = previousThreshold.ToString("0.#####");
        }
    }

    private void UpdateThresholdUI()
    {
        UpdateThresholdSlider();
        UpdateThresholdInputField();
    }

    private void UpdateThresholdSlider()
    {
        if (threshold < 0.0001)
            thresholdSlider.value = (float)((threshold - 0.00001) / (0.0001 - 0.00001) * 0.25);
        else if (threshold < 0.001)
            thresholdSlider.value = 0.25f + (float)((threshold - 0.0001) / (0.001 - 0.0001) * 0.25);
        else if (threshold < 0.01)
            thresholdSlider.value = 0.5f + (float)((threshold - 0.001) / (0.01 - 0.001) * 0.25);
        else
            thresholdSlider.value = 0.75f + (float)((threshold - 0.01) / (0.1 - 0.01) * 0.25);
    }

    private void UpdateThresholdInputField()
    {
        thresholdInputField.text = threshold.ToString("0.#####");
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
            Debug.LogWarning($"Failed to start foreground service: {ex.Message}");
        }
    #endif
    }

    [System.Serializable]
    public class Feed
    {
        public string created_at;
        public string field1;
        public string field2;
        public string field3;
    }

    [System.Serializable]
    public class ThingSpeakData
    {
        public Feed[] feeds;
    }
}

public struct Vector3d
{
    public DateTime dateTime;
    public double latitude;
    public double longitude;
    public double altitude;

    public Vector3d(DateTime dateTime, double latitude, double longitude, double altitude)
    {
        this.dateTime = dateTime;
        this.latitude = latitude;
        this.longitude = longitude;
        this.altitude = altitude;
    }
}
