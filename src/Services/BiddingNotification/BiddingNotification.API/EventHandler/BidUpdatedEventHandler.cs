﻿using BiddingNotification.API.Hubs;
using BiddingNotification.API.Models;
using BuildingBlocks.Messaging.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace BiddingNotification.API.EventHandler;

public class BidUpdatedEventHandler 
    (ILogger<BidUpdatedEventHandler> logger)
    : IConsumer<BidUpdatedEvent>
{
    public async Task Consume(ConsumeContext<BidUpdatedEvent> context)
    {
        logger.LogInformation("Integration Event handled: {IntegrationEvent}", context.Message.GetType().Name);
        var bid = MapToBid(context.Message);
        // await sender.query()
    }

    private Bid MapToBid(BidUpdatedEvent message)
    {
        return new Bid
        {
            Id = message.BidId,
            AuctionId = message.AuctionId,
            CustomerId = message.CustomerId,
            Price = message.Price
        };
    }
}
