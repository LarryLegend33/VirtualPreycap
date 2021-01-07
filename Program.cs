using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using FlyCapture2Managed;
using Emgu.CV;
using Emgu.Util;
using Emgu.CV.CvEnum;
using System.Runtime.InteropServices;
using System.Threading;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using RabbitMQ.Client;


namespace VirtualPreycap
{

    class RMQHost
    {
        public IModel channel; 
        public RMQHost()
        {
            var factory = new ConnectionFactory() { HostName = "localhost" };
            var connection = factory.CreateConnection();
            channel = connection.CreateModel(); 
        }
    }


    class Program
    {
        static void PrintBuildInfo()
        {
            FC2Version version = ManagedUtilities.libraryVersion;
            StringBuilder newStr = new StringBuilder();
            newStr.AppendFormat(
                "FlyCapture2 library version: {0}.{1}.{2}.{3}\n",
                version.major, version.minor, version.type, version.build);
            Console.WriteLine(newStr);
        }
        static void PrintCameraInfo(CameraInfo camInfo)
        {
            StringBuilder newStr = new StringBuilder();
            newStr.Append("\n*** CAMERA INFORMATION ***\n");
            newStr.AppendFormat("Serial number - {0}\n", camInfo.serialNumber);
            newStr.AppendFormat("Camera model - {0}\n", camInfo.modelName);
            newStr.AppendFormat("Camera vendor - {0}\n", camInfo.vendorName);
            newStr.AppendFormat("Sensor - {0}\n", camInfo.sensorInfo);
            newStr.AppendFormat("Resolution - {0}\n", camInfo.sensorResolution);
            Console.WriteLine(newStr);
        }

        unsafe void RunSingleCamera(ManagedPGRGuid guid, string save_location, int numImages, RMQHost rmqhost, string queue_ID)
        {
            // Make a RabbitMQ pipe server to pass tail coordinates to

            IDictionary<string, object> args = new Dictionary<string, object>();
            args.Add("x-max-length", 19);
            rmqhost.channel.QueueDeclare(queue: queue_ID,
                                         durable: false,
                                         exclusive: false,
                                         autoDelete: true,
                                         arguments: args);
            string tailcommands = "";
            ManagedCamera cam = new ManagedCamera();
            // Connect to a camera
            cam.Connect(guid);
            // Get the camera information
            CameraInfo camInfo = cam.GetCameraInfo();
            PrintCameraInfo(camInfo);
            // Get embedded image info from camera
            EmbeddedImageInfo embeddedInfo = cam.GetEmbeddedImageInfo();
            // Enable timestamp collection	
            if (embeddedInfo.timestamp.available == true)
            {
                embeddedInfo.timestamp.onOff = true;
            }
            // Set embedded image info to camera
            cam.SetEmbeddedImageInfo(embeddedInfo);
            // Make a 300 Frame Buffer
            FC2Config bufferFrame = cam.GetConfiguration();
            bufferFrame.grabMode = GrabMode.BufferFrames;
            bufferFrame.numBuffers = 300;
            cam.SetConfiguration(bufferFrame);
            // Start capturing images
            cam.StartCapture();
            // Create a raw image

            ManagedImage rawImage = new ManagedImage();
            // Create a converted image
            ManagedImage convertedImage = new ManagedImage();
            System.Drawing.Size framesize = new System.Drawing.Size(1888, 1888);
            CvInvoke.NamedWindow("Prey Capture" + save_location, NamedWindowType.Normal);
            VideoWriter camvid = new VideoWriter(save_location, 0, 60, framesize, false);

            for (int imageCnt = 0; imageCnt < numImages; imageCnt++)
            {
                // Retrieve an image
                cam.RetrieveBuffer(rawImage);
                // Get the timestamp
                TimeStamp timeStamp = rawImage.timeStamp;
                // Convert the raw image
                //      rawImage.Convert(PixelFormat.PixelFormatBgr, convertedImage); // m
                rawImage.Convert(PixelFormat.PixelFormatRaw8, convertedImage); // use raw8 for GH3s but flea3 can be color.
                int rws = (int)convertedImage.rows;
                int cols = (int)convertedImage.cols;
                IntPtr point = (IntPtr)convertedImage.data;

                Mat cvimage = new Mat(framesize, Emgu.CV.CvEnum.DepthType.Cv8U, 1, point, cols);

                // here call out to contour finding function to get tail angle and record an eye angle
                // tailcommands = imageCnt.ToString();
                tailcommands = "Hey Cora!";
                var msg = Encoding.UTF8.GetBytes(tailcommands);

                rmqhost.channel.BasicPublish(exchange: "",
                                             routingKey: queue_ID,
                                             basicProperties: null,
                                             body: msg);


                camvid.Write(cvimage);
                if (imageCnt % 200 == 0)
                {
                    CvInvoke.Imshow("Prey Capture" + save_location, cvimage);
                    CvInvoke.WaitKey(1);
                    Console.WriteLine(imageCnt);
                }
            }
            // Stop capturing images
            cam.StopCapture();
            camvid.Dispose();
            // Disconnect the camera
            Console.WriteLine("Done Brah");
            cam.Disconnect();
            CvInvoke.DestroyAllWindows();
        }
        static void Main(string[] args)
        {
            PrintBuildInfo();
            Program program = new Program();
            SerialPort pyboard = new SerialPort("COM3", 115200);
            pyboard.Open();
            pyboard.WriteLine("import rig_load\r");
            FileStream fileStream;

            ManagedBusManager busMgr = new ManagedBusManager();
            uint numCameras = busMgr.GetNumOfCameras();
            Console.WriteLine("Number of cameras detected: {0}", numCameras);
            Console.WriteLine("Please Enter Experiment ID: ");
            string idstring = Console.ReadLine();
            List<string> save_to = new List<string> { "C:/Users/Deadpool2/PreyCapResults/" + idstring + "_cam1.AVI", "C:/Users/Deadpool2/PreyCapresults/" + idstring + "_cam2.AVI" };
            Console.WriteLine("Please Enter Number of Frames: ");
            string framestring = Console.ReadLine();
            int frames = Convert.ToInt32(framestring);
            RMQHost rabbitMQhost = new RMQHost();

            if (numCameras == 1)
            {
                ManagedPGRGuid guid = busMgr.GetCameraFromIndex(0);
                program.RunSingleCamera(guid, save_to[0], frames, rabbitMQhost, "camera1");
                //pyboard.WriteLine("rig_load.full_experiment(True,True)\r");
            }
            else if (numCameras == 2)
            {
                ManagedPGRGuid camid1 = busMgr.GetCameraFromIndex(0);
                ManagedPGRGuid camid2 = busMgr.GetCameraFromIndex(1);
                Thread camthread1 = new Thread(() => program.RunSingleCamera(camid1, save_to[0], frames, rabbitMQhost, "camera1"));
                camthread1.Start();
                // have to declare this way if your return is a void but you pass variables to the function
                Thread camthread2 = new Thread(() => program.RunSingleCamera(camid2, save_to[1], frames, rabbitMQhost, "camera2"));
                camthread2.Start();
                pyboard.WriteLine("rig_load.full_experiment(True,True)\r");

            }
        }
    }
}
