using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MiniPizzaBot
{
    public class OrderingDialog: ComponentDialog
    {
        private const string OrderingStateProperty = "orderingState";
        private const string PizzaNameValue = "pizzaName";
        private const string PizzaPiecesValue = "pizzaPieces";

        private const string PizzaNamePrompt = "pizzaNamePrompt";
        private const string PizzaPiecesPrompt = "pizzaPiecesPrompt";

        private const int PizzaNameLengthMinValue = 3;
        private const int MinPizzaPieces = 1;
        private const int MaxPizzaPieces = 4;

        private const string OrderDialog = "orderDialog";


        public OrderingDialog(IStatePropertyAccessor<OrderingState> orderingStateAccessor, ILoggerFactory loggerFactory)
            : base(nameof(OrderingDialog))
        {
            OrderingStateAccessor = orderingStateAccessor ?? throw new ArgumentNullException(nameof(orderingStateAccessor));
            
            var waterfallSteps = new WaterfallStep[]
            {
                    InitializeStateStepAsync,
                    PromptForPizzaNameStepAsync,
                    PromptForPizzaPiecesStepAsync,
                    DisplayOrderStateStepAsync,
            };
            AddDialog(new WaterfallDialog(OrderDialog, waterfallSteps));
            AddDialog(new TextPrompt(PizzaNamePrompt, ValidatePizzaName));
            AddDialog(new NumberPrompt<int>(PizzaPiecesPrompt, ValidatePizzaPieces));
        }

        public IStatePropertyAccessor<OrderingState> OrderingStateAccessor { get; }

        private async Task<DialogTurnResult> InitializeStateStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var orderingState = await OrderingStateAccessor.GetAsync(stepContext.Context, () => null);
            if (orderingState == null)
            {
                var orderingStateOpt = stepContext.Options as OrderingState;
                if (orderingStateOpt != null)
                {
                    await OrderingStateAccessor.SetAsync(stepContext.Context, orderingStateOpt);
                }
                else
                {
                    await OrderingStateAccessor.SetAsync(stepContext.Context, new OrderingState());
                }
            }

            return await stepContext.NextAsync();
        }

        private async Task<DialogTurnResult> PromptForPizzaNameStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var orderingState = await OrderingStateAccessor.GetAsync(stepContext.Context);
            
            if (orderingState != null && !string.IsNullOrWhiteSpace(orderingState.PizzaName) && orderingState.PizzaPieces.HasValue)
            {
                return await PizzaOrderSummary(stepContext);
            }

            if (string.IsNullOrWhiteSpace(orderingState.PizzaName))
            {                
                var opts = new PromptOptions
                {
                    Prompt = new Activity
                    {
                        Type = ActivityTypes.Message,
                        Text = "What pizza do you take?",
                    },
                };
                return await stepContext.PromptAsync(PizzaNamePrompt, opts);
            }
            else
            {
                return await stepContext.NextAsync();
            }
        }

        private async Task<DialogTurnResult> PromptForPizzaPiecesStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var orderingState = await OrderingStateAccessor.GetAsync(stepContext.Context);
            var pizzaName = stepContext.Result as string;

            if (string.IsNullOrWhiteSpace(orderingState.PizzaName) && pizzaName != null)
            {
                orderingState.PizzaName = pizzaName;
                await OrderingStateAccessor.SetAsync(stepContext.Context, orderingState);
            }

            if (!orderingState.PizzaPieces.HasValue)
            {
                var opts = new PromptOptions
                {
                    Prompt = new Activity
                    {
                        Type = ActivityTypes.Message,
                        Text = $"How many pieces?",
                    },
                };
                return await stepContext.PromptAsync(PizzaPiecesPrompt, opts);
            }
            else
            {
                return await stepContext.NextAsync();
            }
        }

        private async Task<DialogTurnResult> DisplayOrderStateStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var orderingState = await OrderingStateAccessor.GetAsync(stepContext.Context);

            var pizzaPieces = stepContext.Result as int?;

            if (!orderingState.PizzaPieces.HasValue && pizzaPieces.HasValue)
            {
                orderingState.PizzaPieces = pizzaPieces;
                await OrderingStateAccessor.SetAsync(stepContext.Context, orderingState);
            }

            return await PizzaOrderSummary(stepContext);
        }

        private async Task<bool> ValidatePizzaName(PromptValidatorContext<string> promptContext, CancellationToken cancellationToken)
        {
            var value = promptContext.Recognized.Value?.Trim() ?? string.Empty;
            if (value.Length >= PizzaNameLengthMinValue)
            {
                promptContext.Recognized.Value = value;
                return true;
            }
            else
            {
                await promptContext.Context.SendActivityAsync($"Names of pizzas needs to be at least `{PizzaNameLengthMinValue}` characters long.");
                return false;
            }
        }

        private async Task<bool> ValidatePizzaPieces(PromptValidatorContext<int> promptContext, CancellationToken cancellationToken)
        {            
            var value = promptContext.Recognized.Value;

            if (value >= MinPizzaPieces && value <= MaxPizzaPieces)
            {
                promptContext.Recognized.Value = value;
                return true;
            }
            else
            {
                await promptContext.Context.SendActivityAsync($"The number of pizza pieces should be between `{MinPizzaPieces}` and `{MaxPizzaPieces}`.");
                return false;
            }
        }

        private async Task<DialogTurnResult> PizzaOrderSummary(WaterfallStepContext stepContext)
        {
            var context = stepContext.Context;
            var orderingState = await OrderingStateAccessor.GetAsync(context);
            
            await context.SendActivityAsync($"Your order: {orderingState.PizzaName} {orderingState.PizzaPieces}");
            return await stepContext.EndDialogAsync();
        }
    }
}
