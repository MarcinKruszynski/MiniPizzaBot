// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MiniPizzaBot
{
    public class MiniPizzaBotBot : IBot
    {
        private readonly ILogger _logger;

        public const string OrderingIntent = "Ordering";
        public const string CancelIntent = "Cancel";
        public const string HelpIntent = "Help";
        public const string NoneIntent = "None";        

        public static readonly string LuisConfiguration = "mini-pizza-bot-LUIS";

        private readonly IStatePropertyAccessor<OrderingState> _orderingStateAccessor;
        private readonly IStatePropertyAccessor<DialogState> _dialogStateAccessor;
        private readonly UserState _userState;
        private readonly ConversationState _conversationState;
        private readonly BotServices _services;

        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        /// <param name="accessors">A class containing <see cref="IStatePropertyAccessor{T}"/> used to manage state.</param>
        /// <param name="loggerFactory">A <see cref="ILoggerFactory"/> that is hooked to the Azure App Service provider.</param>
        /// <seealso cref="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-2.1#windows-eventlog-provider"/>
        public MiniPizzaBotBot(BotServices services, UserState userState, ConversationState conversationState, ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null)
            {
                throw new System.ArgumentNullException(nameof(loggerFactory));
            }

            _logger = loggerFactory.CreateLogger<MiniPizzaBotBot>();
            _logger.LogTrace("Turn start.");

            _services = services ?? throw new ArgumentNullException(nameof(services));
            _userState = userState ?? throw new ArgumentNullException(nameof(userState));
            _conversationState = conversationState ?? throw new ArgumentNullException(nameof(conversationState));

            _orderingStateAccessor = _userState.CreateProperty<OrderingState>(nameof(OrderingState));
            _dialogStateAccessor = _conversationState.CreateProperty<DialogState>(nameof(DialogState));

            if (!_services.LuisServices.ContainsKey(LuisConfiguration))
            {
                throw new InvalidOperationException($"The bot configuration does not contain a service type of `luis` with the id `{LuisConfiguration}`.");
            }

            Dialogs = new DialogSet(_dialogStateAccessor);
            Dialogs.Add(new OrderingDialog(_orderingStateAccessor, loggerFactory));
        }

        private DialogSet Dialogs { get; set; }

        /// <summary>
        /// Every conversation turn for our Echo Bot will call this method.
        /// There are no dialogs used, since it's "single turn" processing, meaning a single
        /// request and response.
        /// </summary>
        /// <param name="turnContext">A <see cref="ITurnContext"/> containing all the data needed
        /// for processing this conversation turn. </param>
        /// <param name="cancellationToken">(Optional) A <see cref="CancellationToken"/> that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A <see cref="Task"/> that represents the work queued to execute.</returns>
        /// <seealso cref="BotStateSet"/>
        /// <seealso cref="ConversationState"/>
        /// <seealso cref="IMiddleware"/>
        public async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default(CancellationToken))
        {
            var activity = turnContext.Activity;

            var dc = await Dialogs.CreateContextAsync(turnContext);

            if (activity.Type == ActivityTypes.Message)
            {
                var luisResults = await _services.LuisServices[LuisConfiguration].RecognizeAsync(dc.Context, cancellationToken);

                var topScoringIntent = luisResults?.GetTopScoringIntent();

                var topIntent = topScoringIntent.Value.intent;

                await UpdateOrderingState(luisResults, dc.Context);

                var interrupted = await IsTurnInterruptedAsync(dc, topIntent);
                if (interrupted)
                {
                    await _conversationState.SaveChangesAsync(turnContext);
                    await _userState.SaveChangesAsync(turnContext);
                    return;
                }

                var dialogResult = await dc.ContinueDialogAsync();

                if (!dc.Context.Responded)
                {
                    switch (dialogResult.Status)
                    {
                        case DialogTurnStatus.Empty:
                            switch (topIntent)
                            {
                                case OrderingIntent:
                                    await dc.BeginDialogAsync(nameof(OrderingDialog));
                                    break;

                                case NoneIntent:
                                default:
                                    await dc.Context.SendActivityAsync("I didn't understand what you just said to me.");
                                    break;
                            }

                            break;

                        case DialogTurnStatus.Waiting:
                            break;

                        case DialogTurnStatus.Complete:
                            await dc.EndDialogAsync();
                            break;

                        default:
                            await dc.CancelAllDialogsAsync();
                            break;
                    }
                }                
            }
            else if (activity.Type == ActivityTypes.ConversationUpdate)
            {
                if (activity.MembersAdded != null)
                {                    
                    foreach (var member in activity.MembersAdded)
                    {
                        
                        if (member.Id != activity.Recipient.Id)
                        {
                            var welcomeCard = CreateAdaptiveCardAttachment();
                            var response = CreateResponse(activity, welcomeCard);
                            await dc.Context.SendActivityAsync(response);                          
                        }
                    }
                }
            }

            await _conversationState.SaveChangesAsync(turnContext);
            await _userState.SaveChangesAsync(turnContext);
        }

        private async Task<bool> IsTurnInterruptedAsync(DialogContext dc, string topIntent)
        {            
            if (topIntent.Equals(CancelIntent))
            {
                if (dc.ActiveDialog != null)
                {
                    await dc.CancelAllDialogsAsync();
                    await dc.Context.SendActivityAsync("Ok. I've canceled our last activity.");
                }
                else
                {
                    await dc.Context.SendActivityAsync("I don't have anything to cancel.");
                }

                return true;        
            }

            if (topIntent.Equals(HelpIntent))
            {
                await dc.Context.SendActivityAsync("Let me try to provide some help.");
                await dc.Context.SendActivityAsync("I order pizzas on Fridays, being asked for help, or being asked to cancel what I am doing.");
                if (dc.ActiveDialog != null)
                {
                    await dc.RepromptDialogAsync();
                }

                return true;        
            }

            return false;           
        }

        private Activity CreateResponse(Activity activity, Attachment attachment)
        {
            var response = activity.CreateReply();
            response.Attachments = new List<Attachment>() { attachment };
            return response;
        }

        private Attachment CreateAdaptiveCardAttachment()
        {
            var adaptiveCard = File.ReadAllText(@".\Dialogs\Welcome\Resources\welcomeCard.json");
            return new Attachment()
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                Content = JsonConvert.DeserializeObject(adaptiveCard),
            };
        }

        private async Task UpdateOrderingState(RecognizerResult luisResult, ITurnContext turnContext)
        {
            if (luisResult.Entities != null && luisResult.Entities.HasValues)
            {
                var orderingState = await _orderingStateAccessor.GetAsync(turnContext, () => new OrderingState());
                var entities = luisResult.Entities;

                // LUIS Entities
                string[] pizzaNameEntities = { "pizzaName", "pizzaName_patternAny" };
                string[] pizzaPiecesEntities = { "number", "pizzaPieces_patternAny" };
                
                foreach (var name in pizzaNameEntities)
                {                    
                    if (entities[name] != null)
                    {                        
                        orderingState.PizzaName = (string)entities[name][0];
                        break;
                    }
                }

                foreach (var pieces in pizzaPiecesEntities)
                {
                    if (entities[pieces] != null)
                    { 
                        if (byte.TryParse((string)entities[pieces][0], out byte num))
                        {
                            orderingState.PizzaPieces = num;
                            break;
                        }                        
                    }
                }
                
                await _orderingStateAccessor.SetAsync(turnContext, orderingState);
            }
        }
    }
}
