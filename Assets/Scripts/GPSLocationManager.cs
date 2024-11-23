using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;

public class GPSLocationManager : MonoBehaviour
{
    public string thingSpeakChannelId = "2729123";
    public string thingSpeakReadAPIKey = "FI6RJNDMMI3R2PX8";
    public string mapboxAccessToken = "pk.eyJ1IjoiaWFtbGV2aW4iLCJhIjoiY20zNTVneHZxMDE0NDJuc2FuazBsMXNqcCJ9.ufMmWdL6ivNgDSKCMxb5iw";
    private float latitude;
    private float longitude;
    private int zoomLevel = 15;
    private int mapWidth = 1000;
    private int mapHeight = 1000;

    public Text latitudeText;
    public Text longitudeText;
    public RawImage mapImage; 

    private void Start()
    {
        StartCoroutine(GetGPSData());
    }

    private IEnumerator GetGPSData()
    {
        while (true)
        {
            string url = $"https://api.thingspeak.com/channels/{thingSpeakChannelId}/feeds.json?api_key={thingSpeakReadAPIKey}&results=1";
            UnityWebRequest www = UnityWebRequest.Get(url);
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.Log("Error: " + www.error);
            }
            else
            {
                // Parse the JSON response
                string jsonText = www.downloadHandler.text;
                ThingSpeakData data = JsonUtility.FromJson<ThingSpeakData>(jsonText);

                // Extract latitude and longitude as floats
                latitude = float.Parse(data.channel.latitude);
                longitude = float.Parse(data.channel.longitude);

                latitudeText.text = latitude.ToString("f6");
                longitudeText.text = longitude.ToString("f6");
            }
            StartCoroutine(GetMapImage());
            yield return new WaitForSeconds(7.5f);
        }
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
        }
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
