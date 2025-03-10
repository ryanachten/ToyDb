using AutoMapper;
using Data = ToyDbContracts.Data;
using Routing = ToyDbContracts.Routing;

namespace ToyDbRouting.Utilities;

public class AutoMapperProfile : Profile
{
    public AutoMapperProfile()
    {
        CreateMap<Data.KeyValueResponse, Routing.KeyValueResponse>();
        CreateMap<Routing.KeyValueRequest, Data.KeyValueRequest>();
    }
}
