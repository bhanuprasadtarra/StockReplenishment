using AutoMapper;
using StockReplenishment.Api.Models.Domain;
using StockReplenishment.Api.Models.Dto;

namespace StockReplenishment.Api.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<ReplenishmentRequest, RequestDto>().ReverseMap();
        CreateMap<RequestLineItem, LineItemDto>().ReverseMap();
    }
}
