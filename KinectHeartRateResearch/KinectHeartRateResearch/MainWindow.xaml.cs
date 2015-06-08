using Microsoft.Kinect;
using Microsoft.Kinect.Face;
using RDotNet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace KinectHeartRateResearch
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private KinectSensor m_Sensor;
        private MultiSourceFrameReader m_MSFReader;
        private CoordinateMapper m_CoordMapper;
        private Body m_currentTrackedBody;
        private ulong m_CurrentTrackingId;
        private FaceFrameSource m_FaceSource;
        private Microsoft.Kinect.Face.FaceFrameReader m_FaceReader;
        private WriteableBitmap m_colorBitmap = null;
        private RectI m_drawingRegion;
        private System.IO.FileStream m_fileStream;
        private string m_filePath;
        private System.Timers.Timer m_timer;
        private bool m_timerStarted;
        private int m_countdown;
        private string countdownText;
        private const int INITIAL_COUNTDOWN = 30;
        private System.Diagnostics.Stopwatch m_secondsElapsed;
        private bool m_JADE_Loaded;
        private REngine engine;

        public string CountdownText
        {
            get
            {
                return this.countdownText;
            }

            set
            {                
                    this.countdownText = value;
                    timeRemaining.Text = value;
                
            }
        }
        public MainWindow()
        {
            InitializeComponent();
            //PropertyChanged += MainWindow_PropertyChanged;
            // use the window object as the view model in this simple example
            this.DataContext = this;
            
            m_timer = new System.Timers.Timer(1000);            
            m_timer.Elapsed += M_timer_Elapsed;
            m_countdown = INITIAL_COUNTDOWN;
            CountdownText = string.Format("Time Remaining: {0} Seconds", m_countdown);
            btnCalculateRate.IsEnabled = false;
            m_secondsElapsed = new System.Diagnostics.Stopwatch();

            InitializeR();
        }

        private void InitializeR()
        {
            engine = REngine.GetInstance();
            engine.Initialize();
            var currentDir = System.Environment.CurrentDirectory.Replace('\\', '/');
            if (!m_JADE_Loaded)
            {
                engine.Evaluate("install.packages('JADE', repos='http://cran.rstudio.com/bin/windows/contrib/3.2/JADE_1.9-92.zip'");
                engine.Evaluate("library(JADE)");
                m_JADE_Loaded = true;
            }
            
        }
        private void MainWindow_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
           
        }

        private void initializeFile()
        {
            m_filePath = string.Format("{0}\\NormHeartRate_{1}{2}{3}{4}{5}{6}.{7}.csv", System.Environment.CurrentDirectory, DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second, DateTime.Now.Millisecond);


            m_fileStream = new System.IO.FileStream(m_filePath, System.IO.FileMode.CreateNew, System.IO.FileAccess.ReadWrite, System.IO.FileShare.ReadWrite, 512, true);
            //string header = "nAlpha,nRed,nGreen,nBlue,nIr\n";
            string header = "nMillisecondsElapsed,nBlue,nGreen,nRed,nAlpha,nIr\n";
            var headerBytes = System.Text.Encoding.UTF8.GetBytes(header);
            m_fileStream.Write(headerBytes, 0, headerBytes.Length);
        }

        private void M_timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            --m_countdown;
            if(m_countdown == 0)
            {
                m_secondsElapsed.Stop();
                m_timer.Stop();
                m_countdown = INITIAL_COUNTDOWN;
               
                this.Dispatcher.Invoke(
                           delegate ()
                           {
                               lblColorFeeds.Text = "Data has been captured. Please wait while we process the signals...";
                               btnCalculateRate.IsEnabled = true;
                               closeFile();
                               m_timerStarted = false;
                               ProcessData();
                           });

                
            }
            this.Dispatcher.Invoke(
                           delegate ()
                           {
                               CountdownText = string.Format("Time Remaining: {0} Seconds", m_countdown);
                           });

        }


        private void ProcessData()
        {
            var currentDir = System.Environment.CurrentDirectory.Replace('\\', '/');
#if DEBUG_TEST
                engine.Evaluate(string.Format("heartRateData <- read.csv('{0}/NormHeartRate_r61.csv')", currentDir.Replace('\\', '/')));
#else

            engine.Evaluate(string.Format("heartRateData <- read.csv('{0}')", m_filePath.Replace('\\', '/')));
#endif
                engine.Evaluate(string.Format("source('{0}/RScripts/KinectHeartRate_JADE.r')", currentDir));
                NumericVector hrVect1 = engine.GetSymbol("hr1").AsNumeric();
                NumericVector hrVect4 = engine.GetSymbol("hr4").AsNumeric();
                double hr1 = hrVect1.First();
                double hr4 = hrVect4.First();

                double hr = (hr1 > hr4) ? hr1 : hr4;
                lblRate.Text = ((int)hr).ToString();
                lblColorFeeds.Text = "Signal processed.";
            System.IO.File.Delete(m_filePath);


        }

        private void btnCalculateRate_Click(object sender, RoutedEventArgs e)
        {

#if DEBUG_TEST
            ProcessData();
#else
            lblRate.Text = "";
            initializeFile();
            //StartCalculatingHeartRate
            //Send data to AzureML
            m_timerStarted = true;
            m_timer.Start();
            btnCalculateRate.IsEnabled = false;
            m_secondsElapsed.Start();
#endif
        }

        private void btnStartKStudioRecording_Click(object sender, RoutedEventArgs e)
        {
            //StartRecordingData
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            //InitializeSensor
            //Initialize Readers
            InitializeSensor();
            
            //StartRecordingData

        }

        private void InitializeSensor()
        {
            m_Sensor = Microsoft.Kinect.KinectSensor.GetDefault();
            m_MSFReader = m_Sensor.OpenMultiSourceFrameReader(FrameSourceTypes.Body | FrameSourceTypes.BodyIndex | FrameSourceTypes.Color | FrameSourceTypes.Infrared | FrameSourceTypes.Depth);
            m_CoordMapper = m_Sensor.CoordinateMapper;
            m_MSFReader.MultiSourceFrameArrived += M_MSFReader_MultiSourceFrameArrived;           
            m_Sensor.Open();

            // create the colorFrameDescription from the ColorFrameSource using Bgra format
            FrameDescription colorFrameDescription = m_Sensor.ColorFrameSource.CreateFrameDescription(ColorImageFormat.Bgra);

            // create the bitmap to display
            this.m_colorBitmap = new WriteableBitmap(colorFrameDescription.Width, colorFrameDescription.Height, 96.0, 96.0, PixelFormats.Bgr32, null);
            imgKinectView.Source = this.ImageSource;



        }


        private void M_MSFReader_MultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            var frameRef = e.FrameReference;
            var msFrame = frameRef.AcquireFrame();
            if (null != msFrame)
            {
                using (var bodyFrame = msFrame.BodyFrameReference.AcquireFrame())
                {
                    ProcessBodyFrame(bodyFrame);
                }
                    using (var colorFrame = msFrame.ColorFrameReference.AcquireFrame())
                    {
                        ProcessColorFrame(colorFrame);
                    }
                
            }
        }

        private void ProcessColorFrame(ColorFrame colorFrame)
        {
            if ( (null == colorFrame)) return;

            FrameDescription colorFrameDescription = colorFrame.FrameDescription;

                    using (KinectBuffer colorBuffer = colorFrame.LockRawImageBuffer())
                    {
                        this.m_colorBitmap.Lock();

                        // verify data and write the new color frame data to the display bitmap
                        if ((colorFrameDescription.Width == this.m_colorBitmap.PixelWidth) && (colorFrameDescription.Height == this.m_colorBitmap.PixelHeight))
                        {
                            colorFrame.CopyConvertedFrameDataToIntPtr(
                                this.m_colorBitmap.BackBuffer,
                                (uint)(colorFrameDescription.Width * colorFrameDescription.Height * 4),
                                ColorImageFormat.Bgra);

                        this.m_colorBitmap.AddDirtyRect(new Int32Rect(0, 0, this.m_colorBitmap.PixelWidth, this.m_colorBitmap.PixelHeight));
                        //this.m_colorBitmap.AddDirtyRect(new Int32Rect(m_drawingRegion.Left, m_drawingRegion.Top, m_drawingRegion.Right, m_drawingRegion.Bottom));
                    }

                        this.m_colorBitmap.Unlock();
                        
                    }
            
            
        }

        private void ProcessBodyFrame(BodyFrame bodyFrame)
        {

            if (null != bodyFrame)
            {
                if (this.m_currentTrackedBody != null)
                {
                    this.m_currentTrackedBody = FindBodyWithTrackingId(bodyFrame, this.m_CurrentTrackingId);

                    if (this.m_currentTrackedBody != null)
                    {
                        return;
                    }
                }

                Body selectedBody = FindClosestBody(bodyFrame);

                if (selectedBody == null)
                {
                    return;
                }

                this.m_currentTrackedBody = selectedBody;
                this.m_CurrentTrackingId = selectedBody.TrackingId;
                //SetupFace
                InitializeFace();
            }
        }

        private void InitializeFace()
        {
            m_FaceSource = new Microsoft.Kinect.Face.FaceFrameSource(m_Sensor, m_CurrentTrackingId, Microsoft.Kinect.Face.FaceFrameFeatures.BoundingBoxInColorSpace | Microsoft.Kinect.Face.FaceFrameFeatures.BoundingBoxInInfraredSpace | Microsoft.Kinect.Face.FaceFrameFeatures.PointsInColorSpace | Microsoft.Kinect.Face.FaceFrameFeatures.PointsInInfraredSpace);
            m_FaceReader = m_FaceSource.OpenReader();
            m_FaceReader.FrameArrived += M_FaceReader_FrameArrived;
           
        }

        private void M_FaceReader_FrameArrived(object sender, Microsoft.Kinect.Face.FaceFrameArrivedEventArgs e)
        {
            var frameRef = e.FrameReference;
            using (var faceFrame = frameRef.AcquireFrame())
            {
                if (faceFrame != null)
                {
                    //get the Color Region
                    if (faceFrame.FaceFrameResult != null)
                    {
                        m_drawingRegion = faceFrame.FaceFrameResult.FaceBoundingBoxInColorSpace;
                        var faceRegion = new RectI();
                        faceRegion.Top = Math.Abs(m_drawingRegion.Top - 36 );
                        faceRegion.Bottom = Math.Abs(m_drawingRegion.Bottom - 12);
                        faceRegion.Left = Math.Abs(m_drawingRegion.Left + 26);
                        faceRegion.Right = Math.Abs(m_drawingRegion.Right - 20);
                        DrawBox(faceRegion);

                        //Take the new region and record ColorFrame Data
                        if (m_timerStarted)
                        {
                            RecordData(faceRegion, faceFrame);
                            lblColorFeeds.Text = "Please be still taking measurements...";
                        }
                        else
                        {
                            lblColorFeeds.Text = "Face Found, Click the Calculate button to start taking measurements...";
                            btnCalculateRate.IsEnabled = true;
                        }
                    }
                }
            }

        }

        private void DrawBox(RectI region)
        {
            if (null == region) return;

            var newLeft = region.Left;
            var newTop = region.Top;
            var newRight = region.Right;
            var newBottom = region.Bottom;
          
            var width = Math.Abs( (newRight+4) - (newLeft));
            var height = Math.Abs(newBottom - newTop);
       
            m_colorBitmap.Lock();
            unsafe {
                byte* pixels = (byte*)m_colorBitmap.BackBuffer;
                for (int i = 0; i < width; i++)
                {
                    for (int j = 0; j < height; j++)
                    {
                        var leftEdge = ((newTop + j) * m_colorBitmap.PixelWidth * 4) + (newLeft);
                        var rightEdge = ((newTop + j) * m_colorBitmap.PixelWidth * 4) + (newRight*4);
                        var index = ((newTop + j) * m_colorBitmap.PixelWidth * 4) + ((newLeft * 4 ) + 4*i);
                        if ((index >= leftEdge) && (index <= rightEdge))
                        {

                           //valid pixels
                            //Blue
                            pixels[index] = 255;
                            
                        //Green
                        //pixels[index + 1] = 255;// (byte)((int)pixels[index + 1] - 100);
                        
                        //Red
                        //pixels[index + 2] = 255;// (byte)((int)pixels[index + 1] + 10);
                            
                        //Alpha - no effect right now
                           // pixels[index + 3] = 50;
                        }
                    }

                }
            }
            m_colorBitmap.Unlock();

        }

        private void RecordData(RectI faceRegion, FaceFrame faceFrame)
        {
            //record the R, G, B, IR values from the Face Region pixels
            using (var irFrame = faceFrame.InfraredFrameReference.AcquireFrame())
            {
                using (var depthFrame = faceFrame.DepthFrameReference.AcquireFrame())
                {
                    using (var colorFrame = faceFrame.ColorFrameReference.AcquireFrame())
                    {
                        if ((null == irFrame) || (null == colorFrame) || (null == depthFrame)) return;

                        DepthSpacePoint[] depthSpacePoints = new DepthSpacePoint[colorFrame.FrameDescription.Height * colorFrame.FrameDescription.Width];

                        FrameDescription depthFrameDescription = depthFrame.FrameDescription;

                        // Access the depth frame data directly via LockImageBuffer to avoid making a copy
                        using (KinectBuffer depthFrameData = depthFrame.LockImageBuffer())
                        {
                            this.m_CoordMapper.MapColorFrameToDepthSpaceUsingIntPtr(
                                depthFrameData.UnderlyingBuffer,
                                depthFrameData.Size,
                                depthSpacePoints);
                        }
                        
                        //Get the pixels                       
                        m_colorBitmap.Lock();
                        unsafe
                        {
                            byte* pixels = (byte*)m_colorBitmap.BackBuffer;

                            var startPixel = faceRegion.Left * faceRegion.Top;
                            var endPixel = faceRegion.Right * faceRegion.Bottom;

                            float alpha = 0;
                            float red = 0;
                            float green = 0;
                            float blue = 0;
                            float ir = 0;

                            ushort[] irFrameData = new ushort[irFrame.FrameDescription.Height * irFrame.FrameDescription.Width];
                            irFrame.CopyFrameDataToArray(irFrameData);

                            //Now get the Red, Green, Blue Pixels for the region
                            for (int i = startPixel; i < endPixel; i += 4)
                            {
                                //var pixel = pixels[i];
                                int irIndex = (int)depthSpacePoints[i].X * (int)depthSpacePoints[i].Y;

                                blue += pixels[i]; // << 24;
                                green += pixels[i + 1]; // << 16;
                                red += pixels[i + 2];// << 8;
                                alpha += pixels[i + 3];
                                if (irIndex < irFrameData.Length)
                                    ir += irFrameData[irIndex];
                                else
                                    ir += 0;
                            }
                            float size = Math.Abs(startPixel - endPixel);

                            float avg_alpha = alpha / size;
                            float avg_red = red / size;
                            float avg_green = green / size;
                            float avg_blue = blue / size;
                            float avg_ir = ir / size;

                            double std_alpha = 0;
                            double std_red = 0;
                            double std_green = 0;
                            double std_blue = 0;
                            double std_ir = 0;

                            double var_alpha = 0;
                            double var_red = 0;
                            double var_green = 0;
                            double var_blue = 0;
                            double var_ir = 0;

                            //Now calculate standard deviation
                            for (int i = startPixel; i < endPixel; i += 4)
                            {
                                //var pixel = pixels[i];
                                var_blue = (double)(pixels[i] - avg_blue);
                                std_blue += Math.Pow(var_blue, 2.0);

                                var_green = (pixels[i + 1] - avg_green);
                                std_green += Math.Pow(var_green, 2);

                                var_red = (pixels[i + 2] - avg_red);
                                std_red += Math.Pow(var_red, 2);

                                var_alpha = (pixels[i + 3] - avg_alpha);
                                std_alpha += Math.Pow(var_alpha, 2);

                                int irIndex = (int)depthSpacePoints[i].X * (int)depthSpacePoints[i].Y;
                                if (irIndex < irFrameData.Length)
                                    var_ir = irFrameData[irIndex] - avg_ir;
                                else
                                    var_ir = avg_ir;

                                std_ir += Math.Pow(var_ir, 2);

                            }

                            std_alpha = Math.Sqrt(std_alpha / size);
                            std_red = Math.Sqrt(std_red / size);
                            std_green = Math.Sqrt(std_green / size);
                            std_blue = Math.Sqrt(std_blue / size);
                            std_ir = Math.Sqrt(std_ir / size);


                            double prime_alpha = 0;
                            double prime_red = 0;
                            double prime_green = 0;
                            double prime_blue = 0;
                            double prime_ir = 0;

                            //Now calculate standard deviation
                            for (int i = startPixel; i < endPixel; i += 4)
                            {
                                //var pixel = pixels[i];
                                var_blue = (double)(pixels[i] - avg_blue);
                                prime_blue += var_blue / std_blue;

                                var_green = (pixels[i + 1] - avg_green);
                                prime_green += var_green / std_green;

                                var_red = (pixels[i + 2] - avg_red);
                                prime_red += var_red / std_red;

                                var_alpha = (pixels[i + 3] - avg_alpha);
                                prime_alpha += var_alpha / std_alpha;

                                int irIndex = (int)depthSpacePoints[i].X * (int)depthSpacePoints[i].Y;
                                if (irIndex < irFrameData.Length)
                                    var_ir = irFrameData[irIndex] - avg_ir;
                                else
                                    var_ir = avg_ir;

                                prime_ir += var_ir / std_ir;
                            }

                            double norm_alpha = prime_alpha / size;
                            double norm_red = prime_red / size;
                            double norm_blue = prime_blue / size;
                            double norm_green = prime_green / size;
                            double norm_ir = prime_ir / size;

                            string data = string.Format("{0},{1},{2},{3},{4},{5}\n", m_secondsElapsed.ElapsedMilliseconds, norm_alpha, norm_red, norm_green, norm_blue, norm_ir);

                            var bytesToWrite = System.Text.Encoding.UTF8.GetBytes(data);
                            m_fileStream.WriteAsync(bytesToWrite, 0, bytesToWrite.Length);
                            //m_fileStream.FlushAsync();

                        }
                        m_colorBitmap.Unlock();
                    }
                }
            }
        }



        /// <summary>
        /// Finds the closest body from the sensor if any
        /// </summary>
        /// <param name="bodyFrame">A body frame</param>
        /// <returns>Closest body, null of none</returns>
        private static Body FindClosestBody(BodyFrame bodyFrame)
        {
            Body result = null;
            double closestBodyDistance = double.MaxValue;

            Body[] bodies = new Body[bodyFrame.BodyCount];
            bodyFrame.GetAndRefreshBodyData(bodies);

            foreach (var body in bodies)
            {
                if (body.IsTracked)
                {
                    var currentLocation = body.Joints[JointType.SpineBase].Position;

                    var currentDistance = VectorLength(currentLocation);

                    if (result == null || currentDistance < closestBodyDistance)
                    {
                        result = body;
                        closestBodyDistance = currentDistance;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Returns the length of a vector from origin
        /// </summary>
        /// <param name="point">Point in space to find it's distance from origin</param>
        /// <returns>Distance from origin</returns>
        private static double VectorLength(CameraSpacePoint point)
        {
            var result = Math.Pow(point.X, 2) + Math.Pow(point.Y, 2) + Math.Pow(point.Z, 2);

            result = Math.Sqrt(result);

            return result;
        }


        /// <summary>
        /// Find if there is a body tracked with the given trackingId
        /// </summary>
        /// <param name="bodyFrame">A body frame</param>
        /// <param name="trackingId">The tracking Id</param>
        /// <returns>The body object, null of none</returns>
        private static Body FindBodyWithTrackingId(BodyFrame bodyFrame, ulong trackingId)
        {
            Body result = null;

            Body[] bodies = new Body[bodyFrame.BodyCount];
            bodyFrame.GetAndRefreshBodyData(bodies);

            foreach (var body in bodies)
            {
                if (body.IsTracked)
                {
                    if (body.TrackingId == trackingId)
                    {
                        result = body;
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Gets the bitmap to display
        /// </summary>
        public ImageSource ImageSource
        {
            get
            {
                return this.m_colorBitmap;
            }
        }

        private void Window_Unloaded(object sender, RoutedEventArgs e)
        {
            //DisposeOfObjects
            DisposeOfObjects();
        }

        private void closeFile()
        {
            if (null != m_fileStream)
            {
                m_fileStream.Flush();
                m_fileStream.Close();
                m_fileStream = null;
            }

        }
        private void DisposeOfObjects()
        {
            engine.Dispose();
            engine = null;

            closeFile();            
            m_colorBitmap = null;

            m_FaceSource.Dispose();
            m_FaceSource = null;

            m_FaceReader.Dispose();
            m_FaceReader = null;
                   
            m_currentTrackedBody = null;

            m_MSFReader.Dispose();
            m_MSFReader = null;
            m_CoordMapper = null;

            m_Sensor.Close();
            m_Sensor = null;            
        }
    }
}
