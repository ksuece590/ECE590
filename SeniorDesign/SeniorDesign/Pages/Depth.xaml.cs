using System;
using System.Collections.Generic;
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
using System.ComponentModel;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Kinect;
using System.IO;

namespace SeniorDesign.Pages
{
    /// <summary>
    /// Interaction logic for Depth.xaml
    /// </summary>
    public partial class Depth : UserControl, INotifyPropertyChanged
    {

        /// <summary>
        /// Map depth range to byte range
        /// </summary>
        private const int MapDepthToByte = 8000 / 256;

        /// <summary>
        /// Active Kinect sensor
        /// </summary>
        private KinectSensor kinectSensor = null;

        /// <summary>
        /// Reader for depth frames
        /// </summary>
        private DepthFrameReader depthFrameReader = null;

        /// <summary>
        /// Description of the data contained in the depth frame
        /// </summary>
        private FrameDescription depthFrameDescription = null;

        /// <summary>
        /// Bitmap to display
        /// </summary>
        private WriteableBitmap depthBitmap = null;

        /// <summary>
        /// The highest value that can be returned in the InfraredFrame.
        /// It is cast to a float for readability in the visualization code.
        /// </summary>
        private const float InfraredSourceValueMaximum = (float)ushort.MaxValue;

        /// </summary>
        /// Used to set the lower limit, post processing, of the
        /// infrared data that we will render.
        /// Increasing or decreasing this value sets a brightness
        /// "wall" either closer or further away.
        /// </summary>
        private const float InfraredOutputValueMinimum = 0.01f;

        /// <summary>
        /// The upper limit, post processing, of the
        /// infrared data that will render.
        /// </summary>
        private const float InfraredOutputValueMaximum = 1.0f;


        /// <summary>
        /// Intermediate storage for frame data converted to color
        /// </summary>
        /// depth frame
        private byte[] depthPixels = null;

        /// <summary>
        /// Current status text to display
        /// </summary>
        private string statusText = null;

        public Depth()
        {
            // get the kinectSensor object
            this.kinectSensor = KinectSensor.GetDefault();

            // open the reader for the depth frames
            this.depthFrameReader = this.kinectSensor.DepthFrameSource.OpenReader();

            // wire handler for frame arrival
            this.depthFrameReader.FrameArrived += this.Reader_FrameArrived;

            // get FrameDescription from DepthFrameSource
            this.depthFrameDescription = this.kinectSensor.DepthFrameSource.FrameDescription;

            // allocate space to put the pixels being received and converted
            this.depthPixels = new byte[this.depthFrameDescription.Width * this.depthFrameDescription.Height];

            // create the bitmap to display
            this.depthBitmap = new WriteableBitmap(this.depthFrameDescription.Width, this.depthFrameDescription.Height, 96.0, 96.0, PixelFormats.Gray8, null);

            // set IsAvailableChanged event notifier
            this.kinectSensor.IsAvailableChanged += this.Sensor_IsAvailableChanged;

            // open the sensor
            this.kinectSensor.Open();

            // set the status text
            this.StatusText = this.kinectSensor.IsAvailable ? Properties.Resources.RunningStatusText
                                                            : Properties.Resources.NoSensorStatusText;

            // use the window object as the view model in this simple example
            this.DataContext = this;

            // initialize the components (controls) of the window
            this.InitializeComponent();
        }



                    /// <summary>
                    /// INotifyPropertyChangedPropertyChanged event to allow window controls to bind to changeable data
                    /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Gets the bitmap to display
        /// </summary>
        public ImageSource ImageSource
        {
            get
            {
                return this.depthBitmap;
            }
        }

        /// <summary>
        /// Gets or sets the current status text to display
        /// </summary>
        public string StatusText
        {
            get
            {
                return this.statusText;
            }

            set
            {
                if (this.statusText != value)
                {
                    this.statusText = value;

                    // notify any bound elements that the text has changed
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("StatusText"));
                    }
                }
            }
        }
        //private void ConvertInfraredDataToPixels()
        //{
        //    // Convert the infrared to RGB
        //    int colorPixelIndex = 0;
        //    for (int i = 0; i < this.infraredFrameData.Length; ++i)
        //    {
        //        // normalize the incoming infrared data (ushort) to a float ranging from InfraredOutputValueMinimum to InfraredOutputValueMaximum] by

        //        // 1. dividing the incoming value by the source maximum value
        //        float intensityRatio = (float)this.infraredFrameData[i] / InfraredSourceValueMaximum;

        //        // 2. dividing by the (average scene value * standard deviations)
        //        intensityRatio /= InfraredSceneValueAverage * InfraredSceneStandardDeviations;

        //        // 3. limiting the value to InfraredOutputValueMaximum
        //        intensityRatio = Math.Min(InfraredOutputValueMaximum, intensityRatio);

        //        // 4. limiting the lower value InfraredOutputValueMinimum
        //        intensityRatio = Math.Max(InfraredOutputValueMinimum, intensityRatio);

        //        // 5. converting the normalized value to a byte and using the result as the RGB components required by the image
        //        byte intensity = (byte)(intensityRatio * 255.0f);
        //        this.infraredPixels[colorPixelIndex++] = intensity; //Blue
        //        this.infraredPixels[colorPixelIndex++] = intensity; //Green
        //        this.infraredPixels[colorPixelIndex++] = intensity; //Red
        //        this.infraredPixels[colorPixelIndex++] = 255;       //Alpha           
        //    }
        //}

        /// <summary>
        /// Directly accesses the underlying image buffer of the DepthFrame to 
        /// create a displayable bitmap.
        /// This function requires the /unsafe compiler option as we make use of direct
        /// access to the native memory pointed to by the depthFrameData pointer.
        /// </summary>
        /// <param name="depthFrameData">Pointer to the DepthFrame image data</param>
        /// <param name="depthFrameDataSize">Size of the DepthFrame image data</param>
        /// <param name="minDepth">The minimum reliable depth value for the frame</param>
        /// <param name="maxDepth">The maximum reliable depth value for the frame</param>
        private unsafe void ProcessDepthFrameData(IntPtr depthFrameData, uint depthFrameDataSize, ushort minDepth, ushort maxDepth)
        {
            // depth frame data is a 16 bit value
            ushort* frameData = (ushort*)depthFrameData;

            //Convert depth to RGB 
            int colorPixelIndex = 0;

            //Variable used to calculate gradient
            //int gradient;

            // convert depth to a visual representation
            //for (int i = 0; i < (int)(depthFrameDataSize / this.depthFrameDescription.BytesPerPixel); ++i)
            for (int i = 0; i < (int)(depthFrameDataSize / this.depthFrameDescription.BytesPerPixel); ++i)
            {
                // Get the depth for this pixel
                ushort depth = frameData[i];


                // To convert to a byte, we're mapping the depth value to the byte range.
                // Values outside the reliable depth range are mapped to 0 (black).
                //depth= (ushort)depth >= minDepth && depth <= maxDepth ? (depth / MapDepthToByte) : 0);
                //float intensityRatio = (float)frameData[i] / (float)ushort.MaxValue;
                //intensityRatio /= (.08f * 3.0f);
                //intensityRatio = Math.Min(maxDepth, intensityRatio);
                //intensityRatio = Math.Max(minDepth, intensityRatio);

                //byte intensity = (byte)(intensityRatio * 255.0f);
                //this.depthPixels[colorPixelIndex++] = intensity;
                //this.depthPixels[colorPixelIndex++] = intensity;
                //this.depthPixels[colorPixelIndex++] = intensity;


                this.depthPixels[i] = (byte)(depth >= minDepth && depth <= maxDepth ? (depth / MapDepthToByte) : 0);
                //this.depthPixels[i] = (byte)128;

                //if (depth > 10000)
                //{
                //    this.depthPixels[colorPixelIndex++] = (byte)0;
                //    this.depthPixels[colorPixelIndex++] = (byte)0;
                //    this.depthPixels[colorPixelIndex++] = (byte)0;
                //}

                //else if (depth > 8000)
                //{
                //    this.depthPixels[colorPixelIndex++] = (byte)255;
                //    this.depthPixels[colorPixelIndex++] = (byte)0;
                //    this.depthPixels[colorPixelIndex++] = (byte)0;
                //}

                //else if (depth > 6000)
                //{
                //    this.depthPixels[colorPixelIndex++] = (byte)0;
                //    this.depthPixels[colorPixelIndex++] = (byte)255;
                //    this.depthPixels[colorPixelIndex++] = (byte)0;
                //}

                //else if (depth > 4000)
                //{
                //    this.depthPixels[colorPixelIndex++] = (byte)0;
                //    this.depthPixels[colorPixelIndex++] = (byte)0;
                //    this.depthPixels[colorPixelIndex++] = (byte)255;
                //}

                //else
                //{
                //    this.depthPixels[colorPixelIndex++] = (byte)255;
                //    this.depthPixels[colorPixelIndex++] = (byte)255;
                //    this.depthPixels[colorPixelIndex++] = (byte)255;
                //}

                // KEEP - Gradient Code
                //else if (depth >= 1000 && depth < 1600)
                //{
                //    //Red to Yellow
                //    if (depth < 1300)
                //    {
                //        this.depthPixels[colorPixelIndex++] = (byte)0;
                //        gradient = ((((int)depth - 1000) * 255) / 300);
                //        this.depthPixels[colorPixelIndex++] = (byte)gradient; //Green increases from 0 to 255
                //        this.depthPixels[colorPixelIndex++] = (byte)255;
                //    }
                //    //Yellow to Green
                //    else
                //    {
                //        this.depthPixels[colorPixelIndex++] = (byte)0;
                //        this.depthPixels[colorPixelIndex++] = (byte)255;
                //        gradient = (255 - ((((int)depth - 1300) * 255) / 300));
                //        this.depthPixels[colorPixelIndex++] = (byte)gradient; //Red decreases from 255 to 0
                //    }
                //}

                // We're outputting BGR, the last byte in the 32 bits is unused so skip it
                //++colorPixelIndex;

            }
        }



        // ConvertInfraredDataToPixels() before this...
        private void RenderPixelArray()
        {
            this.depthBitmap.WritePixels(
                new Int32Rect(0, 0, this.depthBitmap.PixelWidth, this.depthBitmap.PixelHeight),
                this.depthPixels,
                this.depthBitmap.PixelWidth,
                0);

        }

        private void Reader_FrameArrived(object sender, DepthFrameArrivedEventArgs e)
        {
            bool depthFrameProcessed = false;

            using (DepthFrame depthFrame = e.FrameReference.AcquireFrame())
            {
                if (depthFrame != null)
                {
                    // the fastest way to process the body index data is to directly access 
                    // the underlying buffer
                    using (Microsoft.Kinect.KinectBuffer depthBuffer = depthFrame.LockImageBuffer())
                    {
                        // verify data and write the color data to the display bitmap
                        if (((this.depthFrameDescription.Width * this.depthFrameDescription.Height) == (depthBuffer.Size / this.depthFrameDescription.BytesPerPixel)) &&
                            (this.depthFrameDescription.Width == this.depthBitmap.PixelWidth) && (this.depthFrameDescription.Height == this.depthBitmap.PixelHeight))
                        {
                            // Note: In order to see the full range of depth (including the less reliable far field depth)
                            // we are setting maxDepth to the extreme potential depth threshold
                            ushort maxDepth = ushort.MaxValue;

                            // If you wish to filter by reliable depth distance, uncomment the following line:
                            //// maxDepth = depthFrame.DepthMaxReliableDistance

                            this.ProcessDepthFrameData(depthBuffer.UnderlyingBuffer, depthBuffer.Size, depthFrame.DepthMinReliableDistance, maxDepth);
                            depthFrameProcessed = true;
                        }
                    }
                }
            }

            if (depthFrameProcessed)
            {
                this.RenderPixelArray();
            }
        }

        /// <summary>
        /// Handles the event which the sensor becomes unavailable (E.g. paused, closed, unplugged).
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Sensor_IsAvailableChanged(object sender, IsAvailableChangedEventArgs e)
        {
            // on failure, set the status text
            this.StatusText = this.kinectSensor.IsAvailable ? Properties.Resources.RunningStatusText
                                                            : Properties.Resources.SensorNotAvailableStatusText;
        }

        public void UtilizeState(object state)
        {
            throw new NotImplementedException();
        }
    }
}
