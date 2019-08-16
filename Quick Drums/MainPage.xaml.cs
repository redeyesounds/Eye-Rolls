using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Microsoft.Toolkit.Uwp.Input.GazeInteraction;
using Microsoft.Toolkit.Uwp.UI.Controls;
using Windows.Media.Audio;
using Windows.UI.Core;
using Windows.Storage;
using System.Threading.Tasks;
using Windows.Media.Render;
using System.Windows;
using System.Diagnostics;
using Windows.UI.Xaml.Media.Animation;
using System.Drawing;
using Microsoft.Toolkit.Uwp.UI.Controls.TextToolbarSymbols;
using Windows.Devices.Midi;
using Windows.Devices.Enumeration;

// TODO
// Start to overwrite

namespace Quick_Drums
{
    public sealed partial class MainPage : Page
    {
        // Audio code from Mark Heath example
        // https://markheath.net/post/fire-and-forget-uwp-audio-engine
        private AudioGraph audioGraph;
        private AudioDeviceOutputNode outputNode;

        private string currentTrigger = "l";
        private Random random = new Random();

        private string userSetting = "kit";
        private string loopSetting = "toms";

        private static readonly Dictionary<string, string> tom_names = new Dictionary<string, string>()
        {
            {"l", "tom3"}, {"r", "tom2"}
        };
        private static readonly Dictionary<string, string> kit_names = new Dictionary<string, string>()
        {
            {"l", "kick"}, {"r", "snare"}
        };
        private static readonly Dictionary<string, string> cymbal_names = new Dictionary<string, string>()
        {
            {"l", "ride"}, {"r", "hh"}
        };

        private static readonly Dictionary<string, Dictionary<string, string>> all_drums = new Dictionary<string, Dictionary<string, string>>()
        {
            {"toms", tom_names },
            {"kit", kit_names },
            {"cymbals", cymbal_names }
        };

        private static readonly Dictionary<string, string> volume_mappings = new Dictionary<string, string>()
        {
            {"low", "soft"}, {"mid", "medium"}, {"high", "hard"},
        };

        private static readonly Dictionary<string, int> numAudioFiles = new Dictionary<string, int>()
        {
            {"tom3-soft", 4}, {"tom3-medium", 3}, {"tom3-hard", 3},
            {"tom2-soft", 4}, {"tom2-medium", 3}, {"tom2-hard", 2},
            {"snare-soft", 4}, {"snare-medium", 4}, {"snare-hard", 4},
            {"kick-soft", 4}, {"kick-medium", 4}, {"kick-hard", 4},
            {"hh-soft", 4}, {"hh-medium", 4}, {"hh-hard", 5},
            {"ride-soft", 5}, {"ride-medium", 4}, {"ride-hard", 4}
        };

        // Grooves
        //   0 is reserved and empty
        //   1 - 4 are precomposed grooves
        //   5 - 8 (or beyond) are user recordings/snapshots
        private static readonly List<string> groove0 = new List<string>
        {
            "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", ""
        };
        private static readonly List<string> groove1 = new List<string>
        {
            "r_low", "r_low", "r_low", "r_mid", "r_mid", "r_mid", "r_high", "r_high",
            "l_high", "l_high", "l_high", "l_mid", "l_mid", "l_mid", "l_low", "l_low"
        };
        private static readonly List<string> groove2 = new List<string>
        {
            "r_high", "", "l_mid", "r_mid", "", "r_mid", "l_high", "",
            "", "", "l_high", "", "", "", "r_low", ""
        };
        private static readonly List<string> groove3 = new List<string>
        {
            "r_high", "r_high", "r_low", "r_mid", "l_mid", "l_high", "", "",
            "l_high", "l_high", "l_low", "l_mid", "r_mid", "l_high", "", ""
        };
        private static List<string> groove4 = new List<string>
        {
            "l_high", "", "", "l_low", "", "", "l_mid", "",
            "", "l_high", "", "", "r_mid", "", "r_high", ""
        };

        private List<List<string>> grooves = new List<List<string>>
        {
            groove0, groove1, groove2, groove3, groove4
        };

        // Variables for dispatcher music time
        private int currentTempo = 80;
        private int currentGroove = 0;
        private int currentSixteenth = 0;
        private bool grooving = false;
        private readonly DispatcherTimer grooveTimer = new DispatcherTimer();

        // Variables for recording
        private bool recording = false;
        private int offset = 0; // Used in case recording does not start on beat 1 (0)
        private bool clickTrack = false;
        private List<(TimeSpan, string)> recordedOnsets = new List<(TimeSpan, string)>();
        private TimeSpan[] actualBeats = new TimeSpan[16];
        private readonly Stopwatch stopWatch = new Stopwatch();

        private bool firstClick = false;
        private bool secondRecord = false;
        private bool thirdStop = false;

        // Variables for MIDI
        MyMidiDeviceWatcher outputDeviceWatcher;
        List<string> midiOutPortList = new List<string>();
        IMidiOutPort midiOutPort;

        private readonly byte midiChannel = 10;
        private readonly Dictionary<string, byte> midiPitches = new Dictionary<string, byte>() { { "l", 1 }, { "r", 2 } };
        private readonly Dictionary<string, byte> midiVelocities = new Dictionary<string, byte>() { { "low", 5 }, { "mid", 65 }, { "high", 127 } };


        // Variables for animation
        private Dictionary<string, Storyboard> buttonFlashes = new Dictionary<string, Storyboard>(); // computer trigger
        private Dictionary<string, Storyboard> buttonHits = new Dictionary<string, Storyboard>(); // user trigger
        private Button activeDrum = new Button();

        // Variables for split views
        private bool preventClose = false;

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += OnLoaded;

            grooveTimer.Tick += PlayBeat;
            grooveTimer.Interval = new TimeSpan(0, 0, 0, 0, CalculateSixteenthDuration(currentTempo));

            // Attach animation storyboards and remove default gaze UI behavior
            foreach(Button button in LayoutRoot.Children.OfType<Button>())
            {
                string buttonName = button.Name.ToString();
                char buttonSide = buttonName[0];
                
                if (buttonSide == 'l' || buttonSide == 'r' || buttonSide == 'm')
                {
                    RemoveDefaultDwellAppearence(button);
                    AddScaleTransform(button);
                    buttonHits.Add(buttonName, CreateButtonHitAnimation(buttonName));
                }
                if (buttonSide == 'l' || buttonSide == 'r') buttonFlashes.Add(buttonName, CreateButtonAnimation(buttonName));
            }

            // Setup the MIDI Output Device Watcher
            outputDeviceWatcher = new MyMidiDeviceWatcher(MidiOutPort.GetDeviceSelector(), midiOutPortList, Dispatcher, midiToggle);
            outputDeviceWatcher.StartWatcher();

        }

        // ----------------- Audio Processing -----------------

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            var result = await AudioGraph.CreateAsync(new AudioGraphSettings(AudioRenderCategory.Media));
            if (result.Status != AudioGraphCreationStatus.Success) return;
            audioGraph = result.Graph;
            var outputResult = await audioGraph.CreateDeviceOutputNodeAsync();
            if (outputResult.Status != AudioDeviceNodeCreationStatus.Success) return;
            outputNode = outputResult.DeviceOutputNode;
            audioGraph.Start();
        }

        private async Task PlaySound(string file)
        {
            var bassFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri($"ms-appx:///Assets/{file}"));
            var fileInputNodeResult = await audioGraph.CreateFileInputNodeAsync(bassFile);
            if (fileInputNodeResult.Status != AudioFileNodeCreationStatus.Success) return;
            var fileInputNode = fileInputNodeResult.FileInputNode;
            fileInputNode.FileCompleted += FileInputNodeOnFileCompleted;

            fileInputNode.AddOutgoingConnection(outputNode);
        }

        private async void FileInputNodeOnFileCompleted(AudioFileInputNode sender, object args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                sender.RemoveOutgoingConnection(outputNode);
                sender.FileCompleted -= FileInputNodeOnFileCompleted;
                sender.Dispose();
            });
        }

        // Determines a random audio file name before triggering playback
        private async void PlayDrum(string buttonName, bool userTriggered = true)
        {
            string[] buttonComponents = buttonName.Split('_');
            // Only allow User to set current trigger
            if (userTriggered) currentTrigger = buttonComponents[0];

            var triggerLetter = buttonComponents[0];
            var currentPos = buttonComponents[1]; // low, mid, or high

            // MIDI
            byte midiPitch = midiPitches[triggerLetter];
            byte midiVelocity = midiVelocities[currentPos];

            // Check if user triggered or loop triggered
            string drumName;
            if (userTriggered && recording == false) drumName = all_drums[userSetting][triggerLetter];
            else drumName = all_drums[loopSetting][triggerLetter];

            var currentVol = volume_mappings[currentPos];

            var audioFileName = CreateAudioName(drumName, currentVol);
            var newMIDIMessage = new MidiNoteOnMessage(midiChannel, midiPitch, midiVelocity);

            if (midiToggle.IsOn && midiOutPort != null) midiOutPort.SendMessage(newMIDIMessage);
            else await PlaySound(audioFileName);
        }

        private async void PlayClick()
        {
            await PlaySound("click.wav");
        }

        
        private void PlayBeat(object sender, object e)
        {
            HandleRecording(currentSixteenth);

            string currentNote = grooves[currentGroove][currentSixteenth];

            if (currentNote.Length > 0)
            {
                PlayDrum(currentNote, userTriggered:false); // Indicate that the loop is triggering
                buttonFlashes[currentNote].Begin();
            }
                
            BlinkBackgroundOnQuarter(currentSixteenth);
            ClickOnQuarter(currentSixteenth);

            currentSixteenth = (currentSixteenth + 1) % 16;
        }

        // ----------------- Grooving -----------------
        private void StopGroove()
        {
            if (grooving)
            {
                grooveTimer.Stop();
                currentSixteenth = 0;
                currentGroove = 0;
                grooving = false;
            }
        }

        private void PauseGroove()
        {
            if (grooving)
            {
                grooveTimer.Stop();
                grooving = false;
            }
        }

        private void StartGroove()
        {
            if (!grooving)
            {
                grooveTimer.Start();
                grooving = true;
            }
        }

        private void ChangeGroove(int grooveNum)
        {
            currentGroove = grooveNum;
        }

        private void ChangeTempo(int newTempo)
        {
            bool shouldRestartGroove = false;
            if (grooving) shouldRestartGroove = true;

            currentTempo = newTempo;
            PauseGroove();

            grooveTimer.Interval = new TimeSpan(0, 0, 0, 0, CalculateSixteenthDuration(currentTempo));

            if (shouldRestartGroove) StartGroove();
        }


        // ----------------- Recording -----------------

        // This is basically a state machine that handles what to do for the two measures after the record button is pressed
        private void HandleRecording(int sixteenth)
        {
            if (sixteenth == 0)
            {
                if (firstClick)
                {
                    StartClickTrack();
                    firstClick = false;
                    secondRecord = true;
                }
                else if (secondRecord)
                {
                    StartRecording();
                    secondRecord = false;
                    thirdStop = true;
                } 
                else if (thirdStop)
                {
                    StopRecording();
                    thirdStop = false;
                } 
            }
            if (thirdStop) actualBeats[sixteenth] = stopWatch.Elapsed;
        }

        private void StartClickTrack()
        {
            Debug.WriteLine("start clicktrack");

            StopGroove();
            ChangeGroove(0);

            recording = true;
            clickTrack = true;

            firstClick = true;

            recordedOnsets.Clear();

            StartGroove();

        }

        private void StopClickTrack()
        {
            Debug.WriteLine("stop clicktrack");
            clickTrack = false;
        }

        private void StartRecording()
        {
            Debug.WriteLine("Start record");
            offset = currentSixteenth;
            recording = true;
            stopWatch.Start();
        }

        private void RecordEvent(string buttonName)
        {
            TimeSpan elapsedTime = stopWatch.Elapsed;
            recordedOnsets.Add((elapsedTime, buttonName));
            Debug.WriteLine("event");
        }

        // Scheduled via dispatcher timer
        private void StopRecording()
        {
            Debug.WriteLine("stop recording");
            
            // Add the new groove to the list, then add a recording button
            // This will need to move to another function to handle certain logic like overwrite after six recordings
            grooves.Add(MapTimesToGroove(recordedOnsets));
            Button newRecordingButton = AddRecordingButton();
            currentGroove = grooves.Count - 1;
            ResetGrooveButtons();
            newRecordingButton.Background = new SolidColorBrush((Windows.UI.Color)Resources["SystemBaseMediumLowColor"]);
            recordNotify.Show("Recording Saved!", 2000);

            if (!grooving)
            {
                grooveTimer.Start();  // This case should not happen - we will need to consider cases where the user stops in the middle of a recording
                grooving = true;
            }

            stopWatch.Stop();
            recording = false;

            StopClickTrack();
        }

        private List<string> MapTimesToGroove(List<(TimeSpan, string)> onsetTimes)
        {
            int interval = CalculateSixteenthDuration(currentTempo);

            List<string> newGroove = new List<string>
            {
                "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", ""
            };

            for (int sixteenthPos = 0; sixteenthPos < 15; sixteenthPos++)
                Debug.WriteLine((actualBeats[sixteenthPos + 1].TotalMilliseconds - actualBeats[sixteenthPos].TotalMilliseconds) + " : " + interval);

            foreach ((TimeSpan, string) item in onsetTimes)
            {
                TimeSpan beatTime = item.Item1;
                string drumType = item.Item2;
                Debug.WriteLine(beatTime);

                // Handle the first beat
                double firstIntervalToTest = (actualBeats[1].TotalMilliseconds - actualBeats[0].TotalMilliseconds) / 2;
                double firstInterval = (beatTime.TotalMilliseconds - actualBeats[0].TotalMilliseconds);
                if (beatTime.TotalMilliseconds <= actualBeats[0].TotalMilliseconds || (firstInterval < firstIntervalToTest))
                {
                    int posWithOffset = (0 + offset) % 16;
                    Debug.WriteLine(posWithOffset, drumType);
                    newGroove[posWithOffset] = drumType;
                }

                // Handle the last beat
                double lastIntervalToTest = (actualBeats[15].TotalMilliseconds - actualBeats[14].TotalMilliseconds) / 2;
                double lastInterval = (actualBeats[15].TotalMilliseconds - beatTime.TotalMilliseconds);
                if (beatTime.TotalMilliseconds > actualBeats[15].TotalMilliseconds || (lastInterval < lastIntervalToTest))
                {
                    int posWithOffset = (0 + offset) % 16;
                    Debug.WriteLine(posWithOffset, drumType);
                    newGroove[posWithOffset] = drumType;
                }

                // Handle the middle beats
                for (int sixteenthPos = 1; sixteenthPos < 15; sixteenthPos++)
                {
                    double beforeBeatInterval = (actualBeats[sixteenthPos].TotalMilliseconds - actualBeats[sixteenthPos - 1].TotalMilliseconds) / 2;
                    double afterBeatInterval = (actualBeats[sixteenthPos + 1].TotalMilliseconds - actualBeats[sixteenthPos].TotalMilliseconds) / 2;
                    double onsetInterval = Math.Abs(actualBeats[sixteenthPos].TotalMilliseconds - beatTime.TotalMilliseconds);

                    if ((beatTime.TotalMilliseconds <= actualBeats[sixteenthPos].TotalMilliseconds && onsetInterval < beforeBeatInterval) || (beatTime.TotalMilliseconds > actualBeats[sixteenthPos].TotalMilliseconds && onsetInterval < afterBeatInterval))
                    {
                        int posWithOffset = (sixteenthPos + offset) % 16;
                        Debug.WriteLine(posWithOffset, drumType);
                        //Debug.WriteLine(distance);
                        newGroove[posWithOffset] = drumType;
                        break;
                    }
                }
            }

            return newGroove;
        }

        private void ClickOnQuarter(int sixteenth)
        {
            if (!clickTrack) return;
            if (sixteenth == 0 || sixteenth == 4 || sixteenth == 8 || sixteenth == 12) PlayClick();
        }

        // ----------------- Click Events -----------------

        private void OuterClick(object sender, RoutedEventArgs e)
        {
            if ((sender as Button).Name.ToString() == activeDrum.Name.ToString()) return;
            activeDrum = (sender as Button); // prevents repeat triggering, but also worsens mouse performance

            string buttonName = (sender as Button).Name.ToString();
            PlayDrum(buttonName);

            if (recording) RecordEvent(buttonName);

            buttonHits[buttonName].Begin();

            UpdateMiddleColor();
        }

        private void InnerClick(object sender, RoutedEventArgs e)
        {
            if ((sender as Button).Name.ToString() == activeDrum.Name.ToString()) return;
            activeDrum = (sender as Button); // prevents repeat triggering, but also worsens mouse performance

            var buttonName = (sender as Button).Name.ToString();
            var buttonNameToUse = currentTrigger + (sender as Button).Name.ToString().Substring(1); // swap 'm' with 'l' or 'r'

            if (recording) RecordEvent(buttonNameToUse);

            buttonHits[buttonName].Begin();
            PlayDrum(buttonNameToUse);
        }

        private void Toggle_Groove(object sender, RoutedEventArgs e)
        {
            int inputGroove = 0;
            Int32.TryParse((sender as Button).Name.ToString().Substring(1), out inputGroove);

            ResetGrooveButtons();
            (sender as Button).Background = new SolidColorBrush((Windows.UI.Color)Resources["SystemBaseMediumLowColor"]);

            ChangeGroove(inputGroove);
            StartGroove();
        }

        private void Change_Drum(object sender, RoutedEventArgs e)
        {
            string[] drum_info = (sender as Button).Name.ToString().Split("_");
            string user_type = drum_info[0];
            string drum_to_use = drum_info[1];

            if (user_type == "user") userSetting = drum_to_use;
            else loopSetting = drum_to_use;

            StackPanel sp = (sender as Button).Parent as StackPanel;
            foreach(Control b in sp.Children)
                b.Background = new SolidColorBrush((Windows.UI.Color)Resources["SystemBaseLowColor"]);

            (sender as Button).Background = new SolidColorBrush((Windows.UI.Color)Resources["SystemBaseMediumLowColor"]);
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            ResetGrooveButtons();
            StopGroove();
        }

        private void Click_ClickTrack(object sender, RoutedEventArgs e)
        {
            StartClickTrack();
        }

        private void Open_Left(object sender, RoutedEventArgs e)
        {
            if (!splitLeft.IsPaneOpen) splitLeft.IsPaneOpen = true;

        }

        private void Open_Right(object sender, RoutedEventArgs e)
        {
            if (!splitRight.IsPaneOpen) splitRight.IsPaneOpen = true;

        }

        private void TempoSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            string tempoString = String.Format("Tempo: {0}", e.NewValue);
            if (tempoVal != null) tempoVal.Text = tempoString;

            ChangeTempo((int)e.NewValue);
        }

        private void IncreaseTempo(object sender, RoutedEventArgs e)
        {
            tempoSlider.Value += 10;
        }

        private void DecreaseTempo(object sender, RoutedEventArgs e)
        {
            tempoSlider.Value -= 10;
        }

        private async void MidiToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var deviceInformationCollection = outputDeviceWatcher.DeviceInformationCollection;
            if (deviceInformationCollection == null) return;
            Debug.WriteLine("here");

            if (deviceInformationCollection.Count <= 1)
            {
                (sender as ToggleSwitch).IsOn = false;
                (sender as ToggleSwitch).IsEnabled = false;
                return;
            }

            // Right now I am setting the MIDI channel automatically to be the first thing after
            // the default (Microsoft GS Wavetable Synth)
            // The Toggle Button is set to never be active unless we have at least two elements
            DeviceInformation devInfo = deviceInformationCollection[1];
            if (devInfo == null) return;

            midiOutPort = await MidiOutPort.FromIdAsync(devInfo.Id);
            if (midiOutPort == null)
            {
                (sender as ToggleSwitch).IsOn = false;
                (sender as ToggleSwitch).IsEnabled = false;
                System.Diagnostics.Debug.WriteLine("Unable to create MidiOutPort from output device");
            }
        }

        // ----------------- UI Functions -----------------
        // Colors - https://superdevresources.com/tools/color-shades
        // #f59822
        // #d0a5e5
        private void UpdateMiddleColor()
        {
            this.m_low.Background = (SolidColorBrush)Resources[currentTrigger + "_low"];
            this.m_mid.Background = (SolidColorBrush)Resources[currentTrigger + "_mid"];
            this.m_high.Background = (SolidColorBrush)Resources[currentTrigger + "_high"];

            this.m_b_low.Fill = (SolidColorBrush)Resources[currentTrigger + "_b_low"];
            this.m_b_mid.Fill = (SolidColorBrush)Resources[currentTrigger + "_b_mid"];
            this.m_b_high.Fill = (SolidColorBrush)Resources[currentTrigger + "_b_high"];
        }

        private List<Button> GetGrooveButtons()
        {
            int count = grooveButtons.Children.Count;
            List<Button> grooveButtonList = new List<Button>();

            for (int i = 0; i < count; i++)
            {
                if (grooveButtons.Children[i] is Button && ((Button)grooveButtons.Children[i]).Name[0] == 'g')
                    grooveButtonList.Add(((Button)grooveButtons.Children[i]));
            }

            return grooveButtonList;
        }

        private List<Button> GetRecordingButtons()
        {
            int count = recordingButtons.Children.Count;
            List<Button> recordingButtonList = new List<Button>();

            for (int i = 0; i < count; i++)
            {
                if (recordingButtons.Children[i] is Button && ((Button)recordingButtons.Children[i]).Name[0] == 'g')
                    recordingButtonList.Add(((Button)recordingButtons.Children[i]));
            }

            return recordingButtonList;
        }

        // In the current setup, this is never done because grooves are fixed
        private Button AddGrooveButton()
        {
            Button newGrooveButton = new Button();
            List<Button> existingGrooveButtons = GetGrooveButtons();
            int numButtons = existingGrooveButtons.Count;
            newGrooveButton.Content = (numButtons + 1);
            newGrooveButton.Name = "g" + (numButtons + 1);
            newGrooveButton.Click += Toggle_Groove;

            // Inherit its properties from existing buttons
            newGrooveButton.Height = existingGrooveButtons[0].Height;
            newGrooveButton.HorizontalAlignment = existingGrooveButtons[0].HorizontalAlignment;
            newGrooveButton.Margin = existingGrooveButtons[0].Margin;

            GazeInput.SetFixationDuration(newGrooveButton, GazeInput.GetFixationDuration(existingGrooveButtons[0]));
            GazeInput.SetDwellDuration(newGrooveButton, GazeInput.GetDwellDuration(existingGrooveButtons[0]));

            grooveButtons.Children.Add(newGrooveButton);

            return newGrooveButton;
        }

        private Button AddRecordingButton()
        {
            Button newRecordingButton = new Button();

            List<Button> existingGrooveButtons = GetGrooveButtons();
            List<Button> existingRecordingButtons = GetRecordingButtons();

            int numGButtons = existingGrooveButtons.Count;
            int numRButtons = existingRecordingButtons.Count;
            newRecordingButton.Content = (numRButtons + 1);
            newRecordingButton.Name = "g" + (numRButtons + numGButtons + 1);
            newRecordingButton.Click += Toggle_Groove;

            // Inherit its properties from existing buttons
            newRecordingButton.Height = existingGrooveButtons[0].Height;
            newRecordingButton.HorizontalAlignment = existingGrooveButtons[0].HorizontalAlignment;
            newRecordingButton.Margin = existingGrooveButtons[0].Margin;

            GazeInput.SetFixationDuration(newRecordingButton, GazeInput.GetFixationDuration(existingGrooveButtons[0]));
            GazeInput.SetDwellDuration(newRecordingButton, GazeInput.GetDwellDuration(existingGrooveButtons[0]));

            // If numButtons is 0 -> delete the tutorial text
            if (numRButtons == 0) recordingButtons.Children.Clear();

            recordingButtons.Children.Add(newRecordingButton);

            return newRecordingButton;
        }

        private void ResetGrooveButtons()
        {
            List<Button> grooveButtonList = GetGrooveButtons();
            foreach (Button btn in grooveButtonList)
                btn.Background = new SolidColorBrush((Windows.UI.Color)Resources["SystemBaseLowColor"]);

            List<Button> recordingButtonList = GetRecordingButtons();
            foreach (Button btn in recordingButtonList)
                btn.Background = new SolidColorBrush((Windows.UI.Color)Resources["SystemBaseLowColor"]);
        }

        private void BlinkBackgroundOnQuarter(int sixteenth)
        {
            if (sixteenth == 0 || sixteenth == 4 || sixteenth == 8 || sixteenth == 12)
            {
                if (recording) r_blink.Begin();
                else blink.Begin();
            }     
        }

        private Storyboard CreateButtonAnimation(string buttonName)
        {
            Storyboard newStory = new Storyboard();
            string newStoryName = buttonName + "_flash";

            Windows.UI.Color baseColor = ((SolidColorBrush)Resources[buttonName]).Color;
            Windows.UI.Color brighterColor = BrightenColor(baseColor, 1.2);

            ColorAnimationUsingKeyFrames brighten = new ColorAnimationUsingKeyFrames();
            Storyboard.SetTargetName(brighten, buttonName);
            Storyboard.SetTargetProperty(brighten, "(Background).(SolidColorBrush.Color)");

            LinearColorKeyFrame b1 = new LinearColorKeyFrame();
            b1.Value = brighterColor;
            b1.KeyTime = TimeSpan.FromMilliseconds(2);

            LinearColorKeyFrame b2 = new LinearColorKeyFrame();
            b2.Value = baseColor;
            b2.KeyTime = TimeSpan.FromMilliseconds(150);

            brighten.KeyFrames.Add(b1);
            brighten.KeyFrames.Add(b2);

            newStory.Children.Add(brighten);

            LayoutRoot.Resources.Add(newStoryName, newStory);

            return newStory;
        }

        private void AddScaleTransform(Button button)
        {
            string buttonName = button.Name.ToString();

            ScaleTransform st = new ScaleTransform();
            st.ScaleX = 1;
            st.ScaleY = 1;

            button.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

            button.RenderTransform = st;

            return;
        }

        private void RemoveDefaultDwellAppearence(Button button)
        {
            GazeElement gazeButtonControl = new GazeElement();
            gazeButtonControl.DwellProgressFeedback += OnInvokeProgress;
            GazeInput.SetGazeElement(button, gazeButtonControl);
        }

        // Removes default dwell square animation effect
        private void OnInvokeProgress(object sender, DwellProgressEventArgs e)
        {
            e.Handled = true;
        }

        private Storyboard CreateButtonHitAnimation(string buttonName)
        {
            Storyboard newStory = new Storyboard();
            string newStoryName = buttonName + "_hit";

            DoubleAnimationUsingKeyFrames hitY = new DoubleAnimationUsingKeyFrames();
            Storyboard.SetTargetName(hitY, buttonName);
            Storyboard.SetTargetProperty(hitY, "(Button.RenderTransform).(ScaleTransform.ScaleY)");

            DoubleAnimationUsingKeyFrames hitX = new DoubleAnimationUsingKeyFrames();
            Storyboard.SetTargetName(hitX, buttonName);
            Storyboard.SetTargetProperty(hitX, "(Button.RenderTransform).(ScaleTransform.ScaleX)");

            LinearDoubleKeyFrame y1 = new LinearDoubleKeyFrame();
            y1.Value = 0.7;
            y1.KeyTime = TimeSpan.FromMilliseconds(5);

            LinearDoubleKeyFrame y2 = new LinearDoubleKeyFrame();
            y2.Value = 1;
            y2.KeyTime = TimeSpan.FromMilliseconds(200);

            LinearDoubleKeyFrame x1 = new LinearDoubleKeyFrame();
            x1.Value = 0.7;
            x1.KeyTime = TimeSpan.FromMilliseconds(5);

            LinearDoubleKeyFrame x2 = new LinearDoubleKeyFrame();
            x2.Value = 1;
            x2.KeyTime = TimeSpan.FromMilliseconds(200);

            hitY.KeyFrames.Add(y1);
            hitY.KeyFrames.Add(y2);

            hitX.KeyFrames.Add(x1);
            hitX.KeyFrames.Add(x2);

            newStory.Children.Add(hitY);
            newStory.Children.Add(hitX);

            newStory.Completed += new EventHandler<object>(ResetActiveButton);

            LayoutRoot.Resources.Add(newStoryName, newStory);

            return newStory;
        }

        // ----------------- SplitView Functions -----------------

        // Fix bug where gaze tries to close splitview before it has completely opened
        // There is another bug where looking outside the splitview does not trigger a close event
        //      Not sure if this causes it or if it is a Gaze Interaction Library flaw
        private void SplitView_PaneOpening(SplitView sender, object args)
        {
            preventClose = true;
        }

        private void SplitView_PaneOpened(SplitView sender, object args)
        {
            preventClose = false;
        }

        private void SplitView_PaneClosing(SplitView sender, SplitViewPaneClosingEventArgs args)
        {
            if (preventClose) args.Cancel = true;
        }

        // ----------------- Helper Functions -----------------

        private string CreateAudioName(string drumName, string volumeName)
        {
            var audioFileType = $"{drumName}-{volumeName}";
            var numFilesForType = numAudioFiles[audioFileType];

            var randomFileNum = (random.Next(numFilesForType)).ToString();
            if (randomFileNum == "0") randomFileNum = "";
            var audioFileName = $"{audioFileType}{randomFileNum}.wav";

            return audioFileName;
        }

        private int CalculateSixteenthDuration(int currentTempo)
        {
            return ((60 * 1000) / currentTempo) / 4;
        }

        private Windows.UI.Color BrightenColor(Windows.UI.Color baseColor, double multiplier)
        {
            int rVal = Math.Min((int)(baseColor.R * multiplier), 255);
            int gVal = Math.Min((int)(baseColor.G * multiplier), 255);
            int bVal = Math.Min((int)(baseColor.B * multiplier), 255);
            Windows.UI.Color newColor = Windows.UI.Color.FromArgb(baseColor.A, (byte)rVal, (byte)gVal, (byte)bVal);

            return newColor;
        }

        // Necessary so that buttons do not get triggered too often
        private void ResetActiveButton(object sender, object e)
        {
            activeDrum = new Button();
        }
    }
}
