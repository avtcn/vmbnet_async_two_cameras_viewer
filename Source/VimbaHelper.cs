/*=============================================================================
  Copyright (C) 2012 - 2016 Allied Vision Technologies.  All Rights Reserved.
  Redistribution of this file, in original or modified form, without
  prior written consent of Allied Vision Technologies is prohibited.
-------------------------------------------------------------------------------
  File:        VimbaHelper.cs
  Description: Implementation file for the VimbaHelper class that demonstrates
               how to implement an asynchronous, continuous image acquisition
               with VimbaNET.
-------------------------------------------------------------------------------
  THIS SOFTWARE IS PROVIDED BY THE AUTHOR "AS IS" AND ANY EXPRESS OR IMPLIED
  WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF TITLE,
  NON-INFRINGEMENT, MERCHANTABILITY AND FITNESS FOR A PARTICULAR  PURPOSE ARE
  DISCLAIMED.  IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY DIRECT,
  INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
  (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
  LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED
  AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR
  TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
  OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
=============================================================================*/

namespace AsynchronousGrab
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using AVT.VmbAPINET;

    /// <summary>
    /// Delegate for the camera list "Callback"
    /// </summary>
    /// <param name="sender">The Sender object (here: this)</param>
    /// <param name="args">The EventArgs.</param>
    public delegate void CameraListChangedHandler(object sender, EventArgs args);

    /// <summary>
    /// Delegate for the Frame received "Callback"
    /// </summary>
    /// <param name="sender">The sender object (here: this)</param>
    /// <param name="args">The FrameEventArgs</param>
    public delegate void FrameReceivedHandler(object sender, FrameEventArgs args);

    /// <summary>
    /// A helper class as a wrapper around Vimba
    /// </summary>
    public class VimbaHelper
    {
        /// <summary>
        /// Amount of Bitmaps in RingBitmap
        /// </summary>
        private const int m_RingBitmapSize = 2;

        /// <summary>
        /// Protector for m_ImageInUse
        /// </summary>
        private static readonly object m_ImageInUseSyncLock = new object();
        private static readonly object m_ImageInUse2SyncLock = new object();

        /// <summary>
        ///  Bitmaps to display images
        /// </summary>
        private static RingBitmap m_RingBitmap = null;
        private static RingBitmap m_RingBitmap2 = null;

        /// <summary>
        /// Signal of picture box that image is used
        /// </summary>
        private static bool m_ImageInUse = true;
        private static bool m_ImageInUse2 = true;

        /// <summary>
        /// Main Vimba API entry object
        /// </summary>
        private Vimba m_Vimba = null;

        /// <summary>
        /// Camera list changed handler
        /// </summary>
        private CameraListChangedHandler m_CameraListChangedHandler = null;

        /// <summary>
        /// Camera object if camera is open
        /// </summary>
        private Camera m_Camera = null;
        private Camera m_Camera2 = null;

        /// <summary>
        /// Flag for determining the availability of a suitable software trigger
        /// </summary>
        private bool m_IsTriggerAvailable = false;
        public bool IsTriggerAvailable
        {
            get { return m_IsTriggerAvailable; }
        }

        /// <summary>
        /// Flag to remember if acquisition is running
        /// </summary>
        private bool m_Acquiring = false;
        private bool m_Acquiring2 = false;

        /// <summary>
        /// Frames received handler
        /// </summary>
        private FrameReceivedHandler m_FrameReceivedHandler = null;
        private FrameReceivedHandler m_FrameReceivedHandler2 = null;

        /// <summary>
        /// Initializes a new instance of the VimbaHelper class.
        /// </summary>
        public VimbaHelper()
        {
            m_RingBitmap = new RingBitmap(m_RingBitmapSize);
            m_RingBitmap2 = new RingBitmap(m_RingBitmapSize);
        }

        /// <summary>
        /// Finalizes an instance of the VimbaHelper class
        /// </summary>
        ~VimbaHelper()
        {
            // Release Vimba API if user forgot to call Shutdown
            this.ReleaseVimba();
        }

        /// <summary>
        /// Gets or sets a value indicating whether an image is displayed or not
        /// </summary>
        public static bool ImageInUse
        {
            get
            {
                lock (m_ImageInUseSyncLock)
                {
                    return m_ImageInUse;
                }
            }

            set
            {
                lock (m_ImageInUseSyncLock)
                {
                    m_ImageInUse = value;
                }
            }
        }

        public static bool ImageInUse2
        {
            get
            {
                lock (m_ImageInUse2SyncLock)
                {
                    return m_ImageInUse2;
                }
            }

            set
            {
                lock (m_ImageInUse2SyncLock)
                {
                    m_ImageInUse2 = value;
                }
            }
        }


        /// <summary>
        /// Gets CameraList
        /// </summary>
        public List<CameraInfo> CameraList
        {
            get
            {
                // Check if API has been started up at all
                if (null == this.m_Vimba)
                {
                    throw new Exception("Vimba is not started.");
                }

                List<CameraInfo> cameraList = new List<CameraInfo>();
                CameraCollection cameras = this.m_Vimba.Cameras;
                foreach (Camera camera in cameras)
                {
                    //cameraList.Add(new CameraInfo(camera.Name, camera.Id));
                    cameraList.Insert(0, new CameraInfo(camera.Name, camera.Id));
                }

                return cameraList;
            }
        }

        /// <summary>
        /// Starts up Vimba API and loads all transport layers
        /// </summary>
        /// <param name="cameraListChangedHandler">The CameraListChangedHandler (delegate)</param>
        public void Startup(CameraListChangedHandler cameraListChangedHandler)
        {
            // Instantiate main Vimba object
            Vimba vimba = new Vimba();

            // Start up Vimba API
            vimba.Startup();
            this.m_Vimba = vimba;

            bool bError = true;
            try
            {
                // Register camera list change delegate
                if (null != cameraListChangedHandler)
                {
                    this.m_Vimba.OnCameraListChanged += this.OnCameraListChange;
                    this.m_CameraListChangedHandler = cameraListChangedHandler;
                }

                bError = false;
            }
            finally
            {
                // Release Vimba API if an error occurred
                if (true == bError)
                {
                    this.ReleaseVimba();
                }
            }
        }

        /// <summary>
        /// Shuts down Vimba API
        /// </summary>
        public void Shutdown()
        {
            // Check if API has been started up at all
            if (null == this.m_Vimba)
            {
                throw new Exception("Vimba has not been started.");
            }

            this.ReleaseVimba();
        }

        /// <summary>
        /// Gets the version of the Vimba API
        /// </summary>
        /// <returns>The version of the Vimba API</returns>
        public string GetVersion()
        {
            if (null == this.m_Vimba)
            {
                throw new Exception("Vimba has not been started.");
            }

            VmbVersionInfo_t version_info = this.m_Vimba.Version;
            return string.Format("{0:D}.{1:D}.{2:D}", version_info.major, version_info.minor, version_info.patch);
        }

        /// <summary>
        /// Opens the camera
        /// </summary>
        /// <param name="id">The camera ID</param>
        public void OpenCamera(string id)
        {
            // Check parameters
            if (null == id)
            {
                throw new ArgumentNullException("id");
            }

            // Check if API has been started up at all
            if (null == this.m_Vimba)
            {
                throw new Exception("Vimba is not started.");
            }

            // Open camera
            if (null == this.m_Camera)
            {
                this.m_Camera = m_Vimba.OpenCameraByID(id, VmbAccessModeType.VmbAccessModeFull);
                if (null == this.m_Camera)
                {
                    throw new NullReferenceException("No camera retrieved.");
                }
            }

            // Determine if a suitable trigger can be found
            m_IsTriggerAvailable = false;
            if (this.m_Camera.Features.ContainsName("TriggerSoftware") && this.m_Camera.Features["TriggerSoftware"].IsWritable())
            {
                EnumEntryCollection entries = this.m_Camera.Features["TriggerSelector"].EnumEntries;
                foreach (EnumEntry entry in entries)
                {
                    if (entry.Name == "FrameStart")
                    {
                        m_IsTriggerAvailable = true;
                        break;
                    }
                }
            }

            // Set the GeV packet size to the highest possible value
            // (In this example we do not test whether this cam actually is a GigE cam)
            try
            {
                this.m_Camera.Features["GVSPAdjustPacketSize"].RunCommand();
                while (false == this.m_Camera.Features["GVSPAdjustPacketSize"].IsCommandDone())
                {
                    // Do Nothing
                }
            }
            catch
            {
                // Do Nothing
            }
        }

        public void OpenCamera2(string id)
        {
            // Check parameters
            if (null == id)
            {
                throw new ArgumentNullException("id");
            }

            // Check if API has been started up at all
            if (null == this.m_Vimba)
            {
                throw new Exception("Vimba is not started.");
            }

            // Open camera
            if (null == this.m_Camera2)
            {
                this.m_Camera2 = m_Vimba.OpenCameraByID(id, VmbAccessModeType.VmbAccessModeFull);
                if (null == this.m_Camera2)
                {
                    throw new NullReferenceException("No camera #2 retrieved.");
                }
            }

/*
            // Set the GeV packet size to the highest possible value
            // (In this example we do not test whether this cam actually is a GigE cam)
            try
            {
                this.m_Camera2.Features["GVSPAdjustPacketSize"].RunCommand();
                while (false == this.m_Camera2.Features["GVSPAdjustPacketSize"].IsCommandDone())
                {
                    // Do Nothing
                }
            }
            catch
            {
                // Do Nothing
            }
*/        }

        // Closes the currently opened camera if it was opened
        public void CloseCamera()
        {
            ReleaseCamera();
        }
        public void CloseCamera2()
        {
            ReleaseCamera2();
        }

        /// <summary>
        /// Starts the continuous image acquisition and opens the camera
        /// Registers the event handler for the new frame event
        /// </summary>
        /// <param name="frameReceivedHandler">The FrameReceivedHandler (delegate)</param>
        public void StartContinuousImageAcquisition(FrameReceivedHandler frameReceivedHandler)
        {
            bool bError = true;
            try
            {
                // Register frame callback
                if (null != frameReceivedHandler)
                {
                    this.m_Camera.OnFrameReceived += this.OnFrameReceived;
                    this.m_FrameReceivedHandler = frameReceivedHandler;
                }

                // Reset member variables
                m_RingBitmap = new RingBitmap(m_RingBitmapSize);
                m_ImageInUse = true;
                this.m_Acquiring = true;

                // Start synchronous image acquisition (grab)
                this.m_Camera.StartContinuousImageAcquisition(3);

                bError = false;
            }
            finally
            {
                // Close camera already if there was an error
                if (true == bError)
                {
                    try
                    {
                        this.ReleaseCamera();
                    }
                    catch
                    {
                        // Do Nothing
                    }
                }
            }
        }

        public void StartContinuousImageAcquisition2(FrameReceivedHandler frameReceivedHandler)
        {
            bool bError = true;
            try
            {
                // Register frame callback
                if (null != frameReceivedHandler)
                {
                    this.m_Camera2.OnFrameReceived += this.OnFrameReceived2;
                    this.m_FrameReceivedHandler2 = frameReceivedHandler;
                }

                // Reset member variables
                m_RingBitmap2 = new RingBitmap(m_RingBitmapSize);
                m_ImageInUse2 = true;
                this.m_Acquiring2 = true;

                // Start synchronous image acquisition (grab)
                this.m_Camera2.StartContinuousImageAcquisition(3);

                bError = false;
            }
            finally
            {
                // Close camera already if there was an error
                if (true == bError)
                {
                    try
                    {
                        this.ReleaseCamera2();
                    }
                    catch
                    {
                        // Do Nothing
                    }
                }
            }
        }

        /// <summary>
        /// Stops the image acquisition
        /// </summary>
        public void StopContinuousImageAcquisition()
        {
            // Check if API has been started up at all
            if (null == this.m_Vimba)
            {
                throw new Exception("Vimba is not started.");
            }

            // Check if no camera is open
            if (null == this.m_Camera)
            {
                throw new Exception("No camera #1 open.");
            }

            // Close camera
            this.ReleaseCamera();
        }
        public void StopContinuousImageAcquisition2()
        {
            // Check if API has been started up at all
            if (null == this.m_Vimba)
            {
                throw new Exception("Vimba is not started.");
            }

            // Check if no camera is open
            if (null == this.m_Camera2)
            {
                throw new Exception("No camera #2 open.");
            }

            // Close camera
            this.ReleaseCamera2();
        }

        /// <summary>
        /// Enables / disables the software trigger
        /// In software trigger mode the acquisition of each individual frame is triggered by the application
        /// in contrast to 'freerun' where every frame triggers it successor.
        /// </summary>
        /// <param name="enable">True to enable the Software trigger or false to change to "Freerun"</param>
        public void EnableSoftwareTrigger(bool enable)
        {
            if (this.m_Camera != null)
            {
                string featureValueSource = string.Empty;
                string featureValueMode = string.Empty;
                if (enable)
                {
                    featureValueMode = "On";
                }
                else
                {
                    featureValueMode = "Off";
                }

                // Set the trigger selector to FrameStart
                this.m_Camera.Features["TriggerSelector"].EnumValue = "FrameStart";
                // Select the software trigger
                this.m_Camera.Features["TriggerSource"].EnumValue = "Software";
                // And switch it on or off
                this.m_Camera.Features["TriggerMode"].EnumValue = featureValueMode;
            }
        }

        /// <summary>
        /// Sends a software trigger to the camera to
        /// </summary>
        public void TriggerSoftwareTrigger()
        {
            if (null != this.m_Camera)
            {
                this.m_Camera.Features["TriggerSoftware"].RunCommand();
            }
        }

        /// <summary>
        /// Convert frame to displayable image and queue it in ring bitmap
        /// </summary>
        /// <param name="frame">The Vimba Frame containing the image</param>
        /// <returns>The Image extracted from the Vimba frame</returns>
        private static Image ConvertFrame(Frame frame)
        {
            if (null == frame)
            {
                throw new ArgumentNullException("frame");
            }

            // Check if the image is valid
            if (VmbFrameStatusType.VmbFrameStatusComplete != frame.ReceiveStatus)
            {
                Console.Error.WriteLine("Incomplete frame received for camera #1. id = " + frame.FrameID + " Reason: " + frame.ReceiveStatus.ToString());
                throw new Exception("Incomplete frame received for camera #1. id = " + frame.FrameID + " Reason: " + frame.ReceiveStatus.ToString());
            }

            Console.WriteLine("Camera #1 new frame received, frame id = " + frame.FrameID);

            // define return variable
            Image image = null;

            // check if current image is in use,
            // if not we drop the frame to get not in conflict with GUI
            if (ImageInUse)
            {
                // Convert raw frame data into image (for image display)
                m_RingBitmap.FillNextBitmap(frame);

                image = m_RingBitmap.Image;
                ImageInUse = false;
            }
            else 
            {
                Console.Error.WriteLine("ConvertFrame1(): skip image update because the previous has not been drawn! frame id = " + frame.FrameID);
            }

            return image;
        }

        private static Image ConvertFrame2(Frame frame)
        {
            if (null == frame)
            {
                throw new ArgumentNullException("frame #2");
            }

            // Check if the image is valid
            if (VmbFrameStatusType.VmbFrameStatusComplete != frame.ReceiveStatus)
            {
                Console.Error.WriteLine("Incomplete frame received for camera #2. id = " + frame.FrameID + " Reason: " + frame.ReceiveStatus.ToString());
                throw new Exception("Incomplete frame received for camera #2. id = " + frame.FrameID + " Reason: " + frame.ReceiveStatus.ToString());
            }

            Console.WriteLine("Camera #2 new frame received, frame id = " + frame.FrameID);

            // define return variable
            Image image = null;

            // check if current image is in use,
            // if not we drop the frame to get not in conflict with GUI
            if (ImageInUse2)
            {
                // Convert raw frame data into image (for image display)
                m_RingBitmap2.FillNextBitmap(frame);

                image = m_RingBitmap2.Image;
                ImageInUse2 = false;
            }
            else 
            {
                Console.Error.WriteLine("ConvertFrame2(): skip image update because the previous has not been drawn! frame id = " + frame.FrameID);
            }

            return image;
        }

        /// <summary>
        ///  Releases the camera
        ///  Shuts down Vimba
        /// </summary>
        private void ReleaseVimba()
        {
            if (null != this.m_Vimba)
            {
                // We can use cascaded try-finally blocks to release the
                // Vimba API step by step to make sure that every step is executed.
                try
                {
                    try
                    {
                        try
                        {
                            // First we release the camera (if there is one)
                            this.ReleaseCamera();
                            this.ReleaseCamera2();
                        }
                        finally
                        {
                            if (null != this.m_CameraListChangedHandler)
                            {
                                this.m_Vimba.OnCameraListChanged -= this.OnCameraListChange;
                            }
                        }
                    }
                    finally
                    {
                        // Now finally shutdown the API
                        this.m_CameraListChangedHandler = null;
                        this.m_Vimba.Shutdown();
                    }
                }
                finally
                {
                    this.m_Vimba = null;
                }
            }
        }

        /// <summary>
        ///  Unregisters the new frame event
        ///  Stops the capture engine
        ///  Closes the camera
        /// </summary>
        private void ReleaseCamera()
        {
            if (null != this.m_Camera)
            {
                // We can use cascaded try-finally blocks to release the
                // camera step by step to make sure that every step is executed.
                try
                {
                    try
                    {
                        try
                        {
                            if (null != this.m_FrameReceivedHandler)
                            {
                                this.m_Camera.OnFrameReceived -= this.OnFrameReceived;
                            }
                        }
                        finally
                        {
                            this.m_FrameReceivedHandler = null;
                            if (true == this.m_Acquiring)
                            {
                                this.m_Acquiring = false;
                                this.m_Camera.StopContinuousImageAcquisition();
                                if (this.IsTriggerAvailable)
                                {
                                    this.EnableSoftwareTrigger(false);
                                }
                            }
                        }
                    }
                    finally
                    {
                        this.m_Camera.Close();
                    }
                }
                finally
                {
                    this.m_Camera = null;
                }
            }
        }
        private void ReleaseCamera2()
        {
            if (null != this.m_Camera2)
            {
                // We can use cascaded try-finally blocks to release the
                // camera step by step to make sure that every step is executed.
                try
                {
                    try
                    {
                        try
                        {
                            if (null != this.m_FrameReceivedHandler2)
                            {
                                this.m_Camera2.OnFrameReceived -= this.OnFrameReceived2;
                            }
                        }
                        finally
                        {
                            this.m_FrameReceivedHandler2 = null;
                            if (true == this.m_Acquiring2)
                            {
                                this.m_Acquiring2 = false;
                                this.m_Camera2.StopContinuousImageAcquisition();
/*                                if (this.IsTriggerAvailable)
                                {
                                    this.EnableSoftwareTrigger(false);
                                }
*/                            }
                        }
                    }
                    finally
                    {
                        this.m_Camera2.Close();
                    }
                }
                finally
                {
                    this.m_Camera2 = null;
                }
            }
        }

        /// <summary>
        /// Handles the Frame Received event
        /// Converts the image to be displayed and queues it
        /// </summary>
        /// <param name="frame">The Vimba frame</param>
        private void OnFrameReceived(Frame frame)
        {
            try
            {
                // Convert frame into displayable image
                Image image = ConvertFrame(frame);

                FrameReceivedHandler frameReceivedHandler = this.m_FrameReceivedHandler;

                if (null != frameReceivedHandler && null != image)
                {
                    Console.WriteLine("OnFrameReceived(): " + frame.FrameID + ", image = " + image.Size
                        + ", frameReceivedHandler = " + frameReceivedHandler);
                    // Report image to user
                    frameReceivedHandler(this, new FrameEventArgs(image, frame.FrameID));
                }
                else
                {
                    Console.WriteLine("OnFrameReceived(): " + frame.FrameID + ", image is null!");
                }
            }
            catch (Exception exception)
            {
                FrameReceivedHandler frameReceivedHandler = this.m_FrameReceivedHandler;
                if (null != frameReceivedHandler)
                {
                    // Report an error to the user
                    frameReceivedHandler(this, new FrameEventArgs(exception, frame!=null?frame.FrameID:0));
                }
            }
            finally
            {
                // We make sure to always return the frame to the API if we are still acquiring
                if (true == this.m_Acquiring)
                {
                    try
                    {
                        this.m_Camera.QueueFrame(frame);
                    }
                    catch (Exception exception)
                    {
                        FrameReceivedHandler frameReceivedHandler = this.m_FrameReceivedHandler;
                        if (null != frameReceivedHandler)
                        {
                            // Report an error to the user
                            frameReceivedHandler(this, new FrameEventArgs(exception));
                        }
                    }
                }
            }
        }
        private void OnFrameReceived2(Frame frame)
        {
            try
            {
                // Convert frame into displayable image
                Image image = ConvertFrame2(frame);

                FrameReceivedHandler frameReceivedHandler = this.m_FrameReceivedHandler2;
                if (null != frameReceivedHandler && null != image)
                {
                    Console.WriteLine("OnFrameReceived2(): " + frame.FrameID + ", image = " + image.Size
                        + ", frameReceivedHandler = " + frameReceivedHandler );
                    // Report image to user
                    frameReceivedHandler(this, new FrameEventArgs(image, frame.FrameID));
                }
                else { 
                    Console.WriteLine("OnFrameReceived2(): " + frame.FrameID + ", image is null!");
                }
            }
            catch (Exception exception)
            {
                FrameReceivedHandler frameReceivedHandler = this.m_FrameReceivedHandler2;
                if (null != frameReceivedHandler)
                {
                    // Report an error to the user
                    frameReceivedHandler(this, new FrameEventArgs(exception, frame!=null?frame.FrameID:0));
                }
            }
            finally
            {
                // We make sure to always return the frame to the API if we are still acquiring
                if (true == this.m_Acquiring2)
                {
                    try
                    {
                        this.m_Camera2.QueueFrame(frame);
                    }
                    catch (Exception exception)
                    {
                        FrameReceivedHandler frameReceivedHandler = this.m_FrameReceivedHandler2;
                        if (null != frameReceivedHandler)
                        {
                            // Report an error to the user
                            frameReceivedHandler(this, new FrameEventArgs(exception));
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Handles the camera list changed event
        /// </summary>
        /// <param name="reason">The Vimba Trigger Type: Camera plugged / unplugged</param>
        private void OnCameraListChange(VmbUpdateTriggerType reason)
        {
            switch (reason)
            {
                case VmbUpdateTriggerType.VmbUpdateTriggerPluggedIn:
                case VmbUpdateTriggerType.VmbUpdateTriggerPluggedOut:
                    {
                        CameraListChangedHandler cameraListChangedHandler = this.m_CameraListChangedHandler;
                        if (null != cameraListChangedHandler)
                        {
                            cameraListChangedHandler(this, EventArgs.Empty);
                        }
                    }

                    break;

                default:
                    break;
            }
        }
    }
}