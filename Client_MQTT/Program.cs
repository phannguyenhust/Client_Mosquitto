using CsvHelper;
using CsvHelper.Configuration;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Exceptions;
using Newtonsoft.Json;
using System.Globalization;
using System.Text;

class Program
{
    private static IMqttClient? mqttClient;
    private static Dictionary<string, Sensor> sensors = new Dictionary<string, Sensor>();
    private static bool showData = true;

    public static async Task Main(string[] args)
    {
        string broker = "192.168.11.104"; //thay thế bằng IPv4 LocalPC
        int port = 1883;
        string clientId = Guid.NewGuid().ToString();
        string topic = "/test"; //thay thế topic đang sử dụng tại Gateway tương ứng

        var factory = new MqttFactory();
        mqttClient = factory.CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(broker, port)
            .WithClientId(clientId)
            .WithCleanSession()
            .Build();

        try
        {
            await ConnectMqttClientAsync(mqttClient, options);

            await mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(topic).Build());
            Console.WriteLine($"Subscribed to topic: {topic}"); //đã subscribe được topic
            mqttClient.ApplicationMessageReceivedAsync += HandleReceivedMessage;  //chờ nhận data
            await Task.Run(() => ShowMenu());
            await ActiveMqttClient(mqttClient);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Critical error: {ex.Message}");
        }
    }
    //Hàm xử lý data
    private static Task HandleReceivedMessage(MqttApplicationMessageReceivedEventArgs e)
    {
        var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
        try
        {
            var data = JsonConvert.DeserializeObject<MqttMessage>(payload);
            var jsonData = JsonConvert.SerializeObject(data, Formatting.Indented);

            if (data?.Data?.Value?.DeviceList != null)
            {
                sensors.Clear();
                foreach (var device in data.Data.Value.DeviceList)
                {
                    if (device.BleAddr != null)
                    {
                        sensors[device.BleAddr] = new Sensor
                        {
                            Mac = device.BleAddr,
                            ModelStr = device.ModelStr ?? "Unknown Model",
                            ScanRssi = device.ScanRssi.ToString()
                        };
                    }
                }
                //hiển thị Data
                if (showData)
                {
                    Console.WriteLine("Dữ liệu cảm biến:");
                    foreach (var sensor in sensors.Values)
                    {
                        Console.WriteLine($"{sensor.Mac}, {sensor.ModelStr}, {sensor.ScanRssi}");
                    }
                }
            }
            else
            {
                Console.WriteLine("No device list available.");
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Failed to process message: {ex.Message}");
            Console.WriteLine($"Payload causing the error: {payload}");
        }
        
        return Task.CompletedTask;
    }
    //Hàm đưa tùy chọn tới người dùng, 1. là hiển thị thứ tự data, 2. là kết thúc console
    private static void ShowMenu()
    {
        while (true)
        {
            Console.WriteLine("\nNhấn phím số để thực hiện hành động:");
            Console.WriteLine("1: Hiển thị thứ tự cảm biến");
            Console.WriteLine("2: Thoát");

            var option = Console.ReadLine()?.Trim();

            switch (option)
            {
                case "1":
                    showData = false; 
                    ShowSensorListAndRequestOrder();
                    showData = true; 
                    break;
                case "2":
                    Console.WriteLine("Thoát chương trình.");
                    Environment.Exit(0);
                    break;
                default:
                    Console.WriteLine("Lựa chọn không hợp lệ.");
                    break;
            }
        }
    }
    private static async Task ActiveMqttClient(IMqttClient? mqttClient)
    {
        var client = mqttClient ?? throw new ArgumentNullException(nameof(mqttClient), "MQTT client is null. Exiting...");

        while (true)
        {
            try
            {
                if (!client.IsConnected)
                {
                    Console.WriteLine("Mất kết nối với MQTT broker.");
                    Console.WriteLine("Đang cố gắng kết nối lại...");
                    await client.ConnectAsync(client.Options);
                    Console.WriteLine("Đã kết nối lại với MQTT broker.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi khi kết nối lại: {ex.Message}");
            }

            await Task.Delay(1000);
        }
    }

    private static async Task ConnectMqttClientAsync(IMqttClient mqttClient, MqttClientOptions options)
    {
        const int maxRetries = 5;
        int retryCount = 0;

        while (retryCount < maxRetries)
        {
            try
            {
                var connectResult = await mqttClient.ConnectAsync(options);
                if (connectResult.ResultCode == MqttClientConnectResultCode.Success)
                {
                    Console.WriteLine("Connected to MQTT broker successfully.");
                    return;
                }
                else
                {
                    Console.WriteLine($"Failed to connect to MQTT broker: {connectResult.ResultCode}");
                    throw new MqttCommunicationException($"Failed to connect: {connectResult.ResultCode}");
                }
            }
            catch (MqttCommunicationException ex)
            {
                Console.WriteLine($"Connection attempt failed: {ex.Message}");

                retryCount++;
                if (retryCount >= maxRetries)
                {
                    Console.WriteLine("Maximum retry attempts reached. Exiting...");
                    throw;
                }

                Console.WriteLine($"Retrying in {retryCount} seconds...");
                await Task.Delay(retryCount * 1000);
            }
        }
    }
    //hàm hiển thị danh sách data theo thứ tự
    private static void ShowSensorListAndRequestOrder()
    {
        Console.WriteLine("Danh sách cảm biến:");
        int i = 1;
        var sensorsList = new List<Sensor>(sensors.Values);

        if (sensorsList.Count == 0)
        {
            Console.WriteLine("Không có dữ liệu cảm biến.");
        }
        else
        {
            foreach (var sensor in sensorsList)
            {
                Console.WriteLine($"{i++}. MAC Address: {sensor.Mac}, Model: {sensor.ModelStr}, RSSI: {sensor.ScanRssi}");
            }

            Console.Write("Nhập mảng thứ tự cảm biến (cách nhau bởi dấu phẩy): ");

            var input = Console.ReadLine()?.Trim() ?? string.Empty;
            //Xử lý mảng
            List<int> indices = input
                .Split(',')
                .Select(num => int.TryParse(num.Trim(), out int n) ? n - 1 : -1)
                .ToList();

            var selectedSensors = indices.Select(index => index >= 0 && index < sensorsList.Count ? sensorsList[index] : null)
                                         .Where(sensor => sensor != null)
                                         .Cast<Sensor>()
                                         .ToList();

            if (selectedSensors.Count == 0)
            {
                Console.WriteLine("Không có cảm biến hợp lệ được chọn.");
            }
            else
            {
                ClearCsvFile(); //Xóa data cũ trước khi lưu data vào file
                WriteCsv(selectedSensors);
            }
        }
    }

    private static void ClearCsvFile()
    {
        File.WriteAllText("sensors_temp.csv", string.Empty);
    }

    private static void WriteCsv(List<Sensor> sensorsList)
    {
        try
        {
            using (var writer = new StreamWriter("sensors_temp.csv"))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)))
            {
                csv.WriteRecords(sensorsList);
            }
            Console.WriteLine("Lưu file CSV thành công");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error writing CSV file: {ex.Message}");
        }
    }

    private class Sensor
    {
        public string Mac { get; set; } = string.Empty;
        public string ModelStr { get; set; } = string.Empty;
        public string ScanRssi { get; set; } = string.Empty;
    }

    private class MqttMessage
    {
        public DataDetails Data { get; set; } = new DataDetails();
    }

    private class DataDetails
    {
        public DataValue Value { get; set; } = new DataValue();
    }

    private class DataValue
    {
        [JsonProperty("device_list")]
        public List<Device> DeviceList { get; set; } = new List<Device>();
    }

    private class Device
    {
        [JsonProperty("modelstr")]
        public string ModelStr { get; set; } = string.Empty;

        [JsonProperty("ble_addr")]
        public string? BleAddr { get; set; }

        [JsonProperty("scan_rssi")]
        public int ScanRssi { get; set; }
    }
}
