using SharpOSC;
using System.Numerics;

namespace VRCVarjoEyeTracking
{
    internal static class Program
    {
        private const string AVATAR_EYE_TRACKING_ADDRESS = "/tracking/eye/CenterVecFull";
        private const string AVATAR_EYE_CLOSENESS_ADDRESS = "/tracking/eye/EyesClosedAmount";
        private const string AVATAR_PARAMETERS_PREFIX = "/avatar/parameters/";
        private const string IP_ADDRESS = "127.0.0.1";
        private const int PORT_SEND = 9000;
        private const int SEND_CYCLE_MILLISECONDS = 25;

        private static VarjoInterface tracker = new VarjoNativeInterface();
        private static EventHandler _applicationIdleHandler;
        private static Thread _sendingThread;
        private static UDPSender _sender;

        private static OscMessage _eyeTrackingMessage;
        private static OscMessage _eyeClosenessMessage;
        private static DisplayData _displayData = new DisplayData();

        private static MainForm MainForm;

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            try
            {

                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                MainForm = MainForm.Instance;

                _applicationIdleHandler = delegate
                {
                    SetupSender();

                    Application.Idle -= _applicationIdleHandler;
                };

                Application.Idle += _applicationIdleHandler;

                Application.Run(MainForm);

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                MainForm.AddLoggerMessage(ex.Message);
            }
        }

        private static void SetupSender()
        {
            if (InitializeTracking())
            {
                _sender = new UDPSender(IP_ADDRESS, PORT_SEND);
                _sendingThread = new Thread(new ThreadStart(SendingLoop));
                _sendingThread.Start();

                Application.ApplicationExit += delegate
                {
                    _sender.Close();
                    tracker.Teardown();
                    _sendingThread.Interrupt();
                    _sendingThread.Join();
                };
            }
        }

        private static void SendingLoop()
        {
            try
            {
                MainForm.AddLoggerMessage("Starting Sending Loop");
                while (true)
                {
                    tracker.Update();
                    GazeData eyeData = tracker.GetGazeData();
                    EyeMeasurements eyeMeasurements = tracker.GetEyeMeasurements();

                    Vector3 leftEye = VarjoInterface.NormalizeVarjoVector(eyeData.leftEye);
                    Vector3 rightEye = VarjoInterface.NormalizeVarjoVector(eyeData.rightEye);

                    //List<object> LeftValues = new List<object>
                    //{
                    //    leftEye.X, leftEye.Y, leftEye.Z
                    //};

                    //List<object> RightValues = new List<object>
                    //{
                    //    rightEye.X, rightEye.Y, rightEye.Z
                    //};

                    float avgCloseness = 1 - ((eyeMeasurements.leftEyeOpenness + eyeMeasurements.rightEyeOpenness) / 2); // Has been reversed as they use different standards
                    float thresholdCloseness = 1 - ((Math.Clamp(eyeMeasurements.leftEyeOpenness * (1 + MainForm.OpenThreshold), 0, 1) + Math.Clamp(eyeMeasurements.leftEyeOpenness * (1 + MainForm.OpenThreshold), 0, 1)) / 2);
                    //get normalized gaze vector to be transformed using the focus distance 
                    Vector3 eyeVectorFull = VarjoInterface.NormalizeVarjoVector(eyeData.gaze);

                    //Multiply all vectors by the focal distance to increase the length of the vector to match focal distance without changing the angle of the vector
                    eyeVectorFull.X *= (float)eyeData.focusDistance;
                    eyeVectorFull.Y *= (float)eyeData.focusDistance;
                    eyeVectorFull.Z *= (float)eyeData.focusDistance;

                    if (MainForm.OutputEnabled)
                    {
                        _displayData.LeftEye = leftEye;
                        _displayData.RightEye = rightEye;
                        _displayData.LeftOpenness = eyeMeasurements.leftEyeOpenness;
                        _displayData.RightOpenness = eyeMeasurements.rightEyeOpenness;
                        _displayData.AvgCloseness = avgCloseness;
                        _displayData.OscClosenss = thresholdCloseness;
                        MainForm.UpdateValues(_displayData);
                    }

                    float closeness = MainForm.ThresholdEnabled ? thresholdCloseness : avgCloseness;

                    

                    _eyeTrackingMessage = new OscMessage(AVATAR_EYE_TRACKING_ADDRESS, eyeVectorFull.X,eyeVectorFull.Y,eyeVectorFull.Z);
                    _eyeClosenessMessage = new OscMessage(AVATAR_EYE_CLOSENESS_ADDRESS, closeness);

                    List<OscMessage> sendingMessages = new List<OscMessage> { _eyeTrackingMessage, _eyeClosenessMessage };

                    ulong time = (ulong)(DateTime.UtcNow - new DateTime(1900, 1, 1)).TotalMilliseconds * 1000;
                    OscBundle bundle = new OscBundle (time)
                    {
                        Messages = sendingMessages,
                        Timestamp = DateTime.Now,
                    };

                    _sender.Send(bundle);

                    Thread.Sleep(SEND_CYCLE_MILLISECONDS);
                }
            }
            catch (Exception ex)
            {
                MainForm.AddLoggerMessage(ex.Message);
                MainForm.AddLoggerMessage(ex.StackTrace);
            }
            finally 
            { 
                _sender.Close();
                tracker.Teardown();
            }
        }

        private static bool InitializeTracking()
        {
            MainForm.AddLoggerMessage(string.Format("Initializing {0} Varjo module", tracker.GetName()));
            bool pipeConnected = tracker.Initialize();
            return pipeConnected;
        }
    }

    public struct DisplayData
    {
        public Vector3 LeftEye;
        public Vector3 RightEye;
        public float LeftOpenness;
        public float RightOpenness;
        public float AvgCloseness;
        public float OscClosenss;
    }
}