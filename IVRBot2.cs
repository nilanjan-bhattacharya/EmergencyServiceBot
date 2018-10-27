namespace EmergencyServicesBot
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using EmergencyServicesBot.Services;
    using Microsoft.Bot.Builder.Calling;
    using Microsoft.Bot.Builder.Calling.Events;
    using Microsoft.Bot.Builder.Calling.ObjectModel.Contracts;
    using Microsoft.Bot.Builder.Calling.ObjectModel.Misc;
    using Newtonsoft.Json;

    public class IVRBot2 : IDisposable, ICallingBot
    {
        // DTMF keys required for each of option, will be used for parsing results of recognize
        private const string Support = "1";
        private static int tracerId = 1;

        private readonly Dictionary<string, CallState> callStateMap = new Dictionary<string, CallState>();

        private readonly MicrosoftCognitiveSpeechService speechService = new MicrosoftCognitiveSpeechService();

        private Dictionary<RootIntent, bool> rootIntentActiveState = new Dictionary<RootIntent, bool>();
        private int? callerId = null;

        public IVRBot2(ICallingBotService callingBotService)
        {
            if (callingBotService == null)
            {
                throw new ArgumentNullException(nameof(callingBotService));
            }

            this.CallingBotService = callingBotService;

            this.CallingBotService.OnIncomingCallReceived += this.OnIncomingCallReceived;
            this.CallingBotService.OnPlayPromptCompleted += this.OnPlayPromptCompleted;
            this.CallingBotService.OnRecordCompleted += this.OnRecordCompleted;
            this.CallingBotService.OnRecognizeCompleted += this.OnRecognizeCompleted;
            this.CallingBotService.OnHangupCompleted += OnHangupCompleted;

            rootIntentActiveState.Add(RootIntent.REGISTER, false);
            rootIntentActiveState.Add(RootIntent.RESCHEDULE, false);
            rootIntentActiveState.Add(RootIntent.CANCEL, false);
            rootIntentActiveState.Add(RootIntent.LOOK_UP, false);


        }

        public ICallingBotService CallingBotService { get; }

        public void Dispose()
        {
            if (this.CallingBotService != null)
            {
                this.CallingBotService.OnIncomingCallReceived -= this.OnIncomingCallReceived;
                this.CallingBotService.OnPlayPromptCompleted -= this.OnPlayPromptCompleted;
                this.CallingBotService.OnRecordCompleted -= this.OnRecordCompleted;
                this.CallingBotService.OnRecognizeCompleted -= this.OnRecognizeCompleted;
                this.CallingBotService.OnHangupCompleted -= OnHangupCompleted;
            }
        }

        private static Task OnHangupCompleted(HangupOutcomeEvent hangupOutcomeEvent)
        {
            hangupOutcomeEvent.ResultingWorkflow = null;
            return Task.FromResult(true);
        }



        private static Workflow SetupRecording(Workflow workflow, string message)
        {
            var id = Guid.NewGuid().ToString();

            var prompt = GetPromptForText(message);
            var record = new Record
            {
                OperationId = id,
                PlayPrompt = prompt,
                MaxDurationInSeconds = 60,
                InitialSilenceTimeoutInSeconds = 3,
                MaxSilenceTimeoutInSeconds = 1,
                PlayBeep = true,
                RecordingFormat = RecordingFormat.Wav,
                StopTones = new List<char> { '#' }
            };
            workflow.Actions = new List<ActionBase> { record };
            return workflow;
        }

        private static PlayPrompt GetPromptForText(string text)
        {
            var prompt = new Prompt { Value = text, Voice = VoiceGender.Male };
            return new PlayPrompt { OperationId = Guid.NewGuid().ToString(), Prompts = new List<Prompt> { prompt } };
        }

        private Task OnIncomingCallReceived(IncomingCallEvent incomingCallEvent)
        {

            this.callStateMap[incomingCallEvent.IncomingCall.Id] = new CallState(incomingCallEvent.IncomingCall.Participants);

            callerId = null;

            incomingCallEvent.ResultingWorkflow.Actions = new List<ActionBase>
                {
                    new Answer { OperationId = Guid.NewGuid().ToString() },
                    GetRecordedMessageFromUser("Welcome, you can use the service to book, reschedule, cancel or know about existing appointments. Please tell your user Id to proceed further.")
                    //record
                };

            return Task.FromResult(true);
        }

        private Task OnPlayPromptCompleted(PlayPromptOutcomeEvent playPromptOutcomeEvent)
        {
            var callState = this.callStateMap[playPromptOutcomeEvent.ConversationResult.Id];

            return Task.FromResult(true);
        }

        private async Task OnRecordCompleted(RecordOutcomeEvent recordOutcomeEvent)
        {
            List<ActionBase> actions = new List<ActionBase>();



            // Convert the audio to text
            string spokenText = string.Empty;
            if (recordOutcomeEvent.RecordOutcome.Outcome == Outcome.Success)
            {

                var record = await recordOutcomeEvent.RecordedContent;

                spokenText = await this.GetTextFromAudioAsync(record);

                var intentRecieved = await MicrosoftCognitiveSpeechService.SendToLUISViaAPI(spokenText);
                
                Intent luisIntent = JsonConvert.DeserializeObject<Intent>(intentRecieved);

                var rootIntent = RootIntentFinder(luisIntent);

                var callState = this.callStateMap[recordOutcomeEvent.ConversationResult.Id];

                await this.SendSTTResultToUser("We detected the following audio: " + spokenText, callState.Participants);
                await this.SendSTTResultToUser("LUIS Intent: " + intentRecieved, callState.Participants);

                if (!luisIntent.topScoringIntent.intent.Equals("Exit", StringComparison.InvariantCultureIgnoreCase) || rootIntentActiveState.Values.Contains(true))
                {
                    if (callerId == null)
                    {
                        int idTemp = ExtractNumbers(spokenText);
                        var userRecognised = BotStubs.AuthenticateUser(idTemp);
                        if (string.IsNullOrEmpty(userRecognised))
                        {
                            SetupRecording(recordOutcomeEvent.ResultingWorkflow, "User id not recognised please retry.");
                        }
                        else
                        {
                            callerId = idTemp;
                            SetupRecording(recordOutcomeEvent.ResultingWorkflow, String.Format("Welcome {0}, please let me know what you want to do.", userRecognised));

                        }
                    }
                    else
                    {
                        if (!rootIntentActiveState[rootIntent])
                        {
                            HandleRootIntent(rootIntent, recordOutcomeEvent.ResultingWorkflow);
                        }
                        else
                        {
                            switch (rootIntent)
                            {
                                case RootIntent.REGISTER:
                                    if (rootIntentActiveState[RootIntent.REGISTER])
                                    {
                                        var dates = BotStubs.GetDates();

                                        var ent = luisIntent.entities[0].resolution.values.FirstOrDefault().value;

                                        if (luisIntent.entities.FirstOrDefault().resolution.values.Length == 1)
                                        {

                                        }
                                        else {
                                            SetupRecording(recordOutcomeEvent.ResultingWorkflow, "Unable to recognise the date for registration please retry.");

                                        }
                                        //SetupRecording(recordOutcomeEvent.ResultingWorkflow, "Please ");
                                        recordOutcomeEvent.ResultingWorkflow.Actions = new List<ActionBase>
                                        {
                                            GetPromptForText("Registration succesfull !!"),
                                            new Hangup { OperationId = Guid.NewGuid().ToString() }

                                        };
                                        recordOutcomeEvent.ResultingWorkflow.Links = null;
                                        this.callStateMap.Remove(recordOutcomeEvent.ConversationResult.Id);


                                    }
                                    break;
                                case RootIntent.RESCHEDULE:
                                case RootIntent.CANCEL:
                                case RootIntent.LOOK_UP:
                                    return;
                            }
                        }
                    }

                }
                else if (luisIntent.topScoringIntent.intent.Equals("Exit", StringComparison.InvariantCultureIgnoreCase) && luisIntent.topScoringIntent.score > 0.80)
                {
                    recordOutcomeEvent.ResultingWorkflow.Actions = new List<ActionBase>
                    {
                        GetPromptForText("Thanks for using emergency bot"),
                        new Hangup { OperationId = Guid.NewGuid().ToString() }

                    };
                    recordOutcomeEvent.ResultingWorkflow.Links = null;
                    this.callStateMap.Remove(recordOutcomeEvent.ConversationResult.Id);
                }

            }
            else
            {
                recordOutcomeEvent.ResultingWorkflow.Actions = actions;
                SetupRecording(recordOutcomeEvent.ResultingWorkflow, "Recording failed please retry.");

            }

        }

        private async Task SendSTTResultToUser(string text, IEnumerable<Participant> participants)
        {
            var to = participants.Single(x => x.Originator);
            var from = participants.First(x => !x.Originator);

            await AgentListener.Resume(to.Identity, to.DisplayName, from.Identity, from.DisplayName, to.Identity, text);
        }

        /// <summary>
        /// Gets text from an audio stream.
        /// </summary>
        /// <param name="audiostream"></param>
        /// <returns>Transcribed text. </returns>
        private async Task<string> GetTextFromAudioAsync(Stream audiostream)
        {
            var text = await this.speechService.GetTextFromAudioAsync(audiostream);
            Debug.WriteLine(text);
            return text;
        }

        private Task OnRecognizeCompleted(RecognizeOutcomeEvent recognizeOutcomeEvent)
        {
            var callState = this.callStateMap[recognizeOutcomeEvent.ConversationResult.Id];


            return Task.FromResult(true);
        }

        private class CallState
        {
            public CallState(IEnumerable<Participant> participants)
            {
                this.Participants = participants;
            }

            public string ChosenMenuOption { get; set; }

            public IEnumerable<Participant> Participants { get; }
        }

        #region NewMethods
        private Record GetRecordedMessageFromUser(string message)
        {
            var record = new Record
            {
                OperationId = Guid.NewGuid().ToString(),
                PlayPrompt = new PlayPrompt { OperationId = Guid.NewGuid().ToString(), Prompts = new List<Prompt> { new Prompt { Value = message } } },
                RecordingFormat = RecordingFormat.Wav
            };

            return record;
        }

        private void HandleRootIntent(RootIntent intent, Workflow workflow)
        {


            switch (intent)
            {
                case RootIntent.REGISTER:
                    rootIntentActiveState[RootIntent.REGISTER] = true;
                    rootIntentActiveState[RootIntent.RESCHEDULE] = false;
                    rootIntentActiveState[RootIntent.CANCEL] = false;
                    rootIntentActiveState[RootIntent.LOOK_UP] = false;

                    //SetupRecording(workflow, "Please " + intent.topScoringIntent.intent);

                    break;
                case RootIntent.RESCHEDULE:
                    rootIntentActiveState[RootIntent.REGISTER] = false;
                    rootIntentActiveState[RootIntent.RESCHEDULE] = true;
                    rootIntentActiveState[RootIntent.CANCEL] = false;
                    rootIntentActiveState[RootIntent.LOOK_UP] = false;
                    break;
                case RootIntent.CANCEL:
                    rootIntentActiveState[RootIntent.REGISTER] = false;
                    rootIntentActiveState[RootIntent.RESCHEDULE] = false;
                    rootIntentActiveState[RootIntent.CANCEL] = true;
                    rootIntentActiveState[RootIntent.LOOK_UP] = false;
                    break;
                case RootIntent.LOOK_UP:
                    rootIntentActiveState[RootIntent.REGISTER] = false;
                    rootIntentActiveState[RootIntent.RESCHEDULE] = false;
                    rootIntentActiveState[RootIntent.CANCEL] = false;
                    rootIntentActiveState[RootIntent.LOOK_UP] = true;
                    break;
            }
        }

        private RootIntent RootIntentFinder(Intent intent)
        {
            if (intent.topScoringIntent.score > 0.5)
            {
                switch (intent.topScoringIntent.intent.ToLower())
                {
                    case "register":
                        return RootIntent.REGISTER;
                    case "reschedule":
                        return RootIntent.RESCHEDULE;
                    case "cancel":
                        return RootIntent.CANCEL;
                    case "lookup":
                        return RootIntent.LOOK_UP;
                    default:
                        return RootIntent.EXIT;

                }
            }
            else
            {
                return RootIntent.UNKNOWN;
            }

        }

        private int ExtractNumbers(string str)
        {
            //const string input = "There are 4 numbers in this string: 40, 30, and 10.";
            // Split on one or more non-digit characters.
            string[] numbers = Regex.Split(str, @"\D+");

            int i = int.Parse(numbers[0]);
            return i;
            //foreach (string value in numbers)
            //{
            //    if (!string.IsNullOrEmpty(value))
            //    {
            //        int i = int.Parse(value);
            //        Console.WriteLine("Number: {0}", i);
            //    }
            //}
        }
        #endregion

    }

    public enum RootIntent
    {
        UNKNOWN = -1,
        EXIT = 0,
        REGISTER,
        RESCHEDULE,
        CANCEL,
        LOOK_UP

    }

}