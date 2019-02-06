// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Hosting;
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
        private IHostingEnvironment _hostingEnvironment;

        public const string OrderingIntent = "ordering_pizza";
        public const string CancelIntent = "cancel";
        public const string HelpIntent = "help";
        public const string NoneIntent = "none";         

        private readonly IStatePropertyAccessor<OrderingState> _orderingStateAccessor;
        private readonly IStatePropertyAccessor<DialogState> _dialogStateAccessor;
        private readonly UserState _userState;
        private readonly ConversationState _conversationState;        

        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        /// <param name="accessors">A class containing <see cref="IStatePropertyAccessor{T}"/> used to manage state.</param>
        /// <param name="loggerFactory">A <see cref="ILoggerFactory"/> that is hooked to the Azure App Service provider.</param>
        /// <seealso cref="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-2.1#windows-eventlog-provider"/>
        public MiniPizzaBotBot(UserState userState, ConversationState conversationState, ILoggerFactory loggerFactory, IHostingEnvironment env)
        {
            if (loggerFactory == null)
            {
                throw new System.ArgumentNullException(nameof(loggerFactory));
            }

            _logger = loggerFactory.CreateLogger<MiniPizzaBotBot>();
            _logger.LogTrace("Turn start.");

            _hostingEnvironment = env;
            
            _userState = userState ?? throw new ArgumentNullException(nameof(userState));
            _conversationState = conversationState ?? throw new ArgumentNullException(nameof(conversationState));

            _orderingStateAccessor = _userState.CreateProperty<OrderingState>(nameof(OrderingState));
            _dialogStateAccessor = _conversationState.CreateProperty<DialogState>(nameof(DialogState));            

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
                //wit.ai
                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer KVTD4BHRL2JYUYGCQ6VUCGNJGBIW2PD4");

                var date = DateTime.Now.ToString("yyyyMMdd");

                var message = HttpUtility.UrlEncode(activity.Text);

                var url = $"https://api.wit.ai/message?v={date}&q={message}";

                var json = await httpClient.GetStringAsync(new Uri(url));

                var witResults = JsonConvert.DeserializeObject<WitResult>(json);

                var topIntent = GetWitIntent(witResults);

                await UpdateOrderingState(witResults, dc.Context);

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
                                    await dc.Context.SendActivityAsync("Nie rozumiem co do mnie mówisz.");
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
                            var welcomeCard = GetHeroCard().ToAttachment(); // CreateAdaptiveCardAttachment();
                            var response = CreateResponse(activity, welcomeCard);
                            await dc.Context.SendActivityAsync(response);                          
                        }
                    }
                }
            }

            await _conversationState.SaveChangesAsync(turnContext);
            await _userState.SaveChangesAsync(turnContext);
        }

        private string GetWitIntent(WitResult witResult)
        {
            if (witResult.entities != null && witResult.entities.intent != null && witResult.entities.intent.Length > 0 &&
                !string.IsNullOrEmpty(witResult.entities.intent[0].value))
                return witResult.entities.intent[0].value;

            return NoneIntent;
        }

        private IDictionary<string, string[]> GetWitEntities(WitResult witResult)
        {
            var entities = new Dictionary<string, string[]>();

            if (witResult.entities != null)
            {
                if (witResult.entities.pizza_name != null && witResult.entities.pizza_name.Length > 0 &&
                    !string.IsNullOrEmpty(witResult.entities.pizza_name[0].value))
                    entities.Add("pizza_name", new string[] { witResult.entities.pizza_name[0].value });

                if (witResult.entities.number != null && witResult.entities.number.Length > 0)
                    entities.Add("number", new string[] { witResult.entities.number[0].value.ToString() });
            }

            return entities;
        }

        private async Task<bool> IsTurnInterruptedAsync(DialogContext dc, string topIntent)
        {            
            if (topIntent.Equals(CancelIntent))
            {
                if (dc.ActiveDialog != null)
                {
                    await dc.CancelAllDialogsAsync();
                    await dc.Context.SendActivityAsync("Ok. Anulowałem ostatnią aktywność.");
                }
                else
                {
                    await dc.Context.SendActivityAsync("Nie mam niczego co mógłbym anulować.");
                }

                return true;        
            }

            if (topIntent.Equals(HelpIntent))
            {
                await dc.Context.SendActivityAsync("Już służę pomocą.");
                await dc.Context.SendActivityAsync("Zamawiam pizze w piątki, udzielam pomocy lub anuluję to co robię.");
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
            //var adaptiveCard = File.ReadAllText(@".\Dialogs\Welcome\Resources\welcomeCard.json");

            var filePath = Path.Combine(_hostingEnvironment.ContentRootPath, @"Dialogs\Welcome\Resources\welcomeCard.json");
            var adaptiveCard = File.ReadAllText(filePath);

            return new Attachment()
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                Content = JsonConvert.DeserializeObject(adaptiveCard),
            };
        }

        private static HeroCard GetHeroCard()
        {
            var heroCard = new HeroCard
            {
                Title = "Pizza Bot",
                Subtitle = "0.2 Wit PL",
                Text = "Dzisiaj Parma!",
                Images = new List<CardImage> { new CardImage("https://cdn.pixabay.com/photo/2016/06/01/12/59/pizza-1428926_640.png") },
                Buttons = new List<CardAction> { new CardAction(ActionTypes.OpenUrl, "Menu", value: "http://www.parmapizza.pl/menu/") },
            };

            return heroCard;
        }

        private async Task UpdateOrderingState(WitResult witResult, ITurnContext turnContext)
        {            
            var orderingState = await _orderingStateAccessor.GetAsync(turnContext, () => new OrderingState());
            var entities = GetWitEntities(witResult);

            // Wit Entities
            string[] pizzaNameEntities = { "pizza_name" };
            string[] pizzaPiecesEntities = { "number" };
                
            foreach (var name in pizzaNameEntities)
            {                    
                if (entities.ContainsKey(name) && entities[name] != null)
                {                        
                    orderingState.PizzaName = entities[name][0];
                    break;
                }
            }

            foreach (var pieces in pizzaPiecesEntities)
            {
                if (entities.ContainsKey(pieces) && entities[pieces] != null)
                { 
                    if (int.TryParse(entities[pieces][0], out int num))
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
