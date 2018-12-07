﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Accelerider.Windows.Infrastructure;
using Accelerider.Windows.Modules.NetDisk.Enumerations;
using Accelerider.Windows.Modules.NetDisk.Interfaces;
using Accelerider.Windows.Modules.NetDisk.Models.Results;
using Accelerider.Windows.TransferService;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Refit;

namespace Accelerider.Windows.Modules.NetDisk.Models.OneDrive
{
    public class OneDriveUser : NetDiskUserBase
    {

        public string RefreshToken { get; }
        public string AccessToken { get; private set; }
        public IMicrosoftGraphApi Api { get; }

        public OneDriveUser(string token)
        {
            RefreshToken = token;
            Api = RestService.For<IMicrosoftGraphApi>(
                new HttpClient(new AuthenticatedHttpClientHandler(() => AccessToken))
                {
                    BaseAddress = new Uri("https://graph.microsoft.com")
                }, new RefitSettings() { JsonSerializerSettings = new JsonSerializerSettings() });
        }

        public override async Task RefreshUserInfoAsync()
        {
            await RefreshAccessToken();
            var info = await Api.GetUserInfoAsync();
            Username = info.Owner["user"].Value<string>("displayName");
            TotalCapacity = info.Quota.Value<long>("total");
            UsedCapacity = info.Quota.Value<long>("used");
        }

        public async Task RefreshAccessToken()
        {
            using (var client = new HttpClient())
            {
                var json = JObject.Parse(
                    await client.GetStringAsync("https://api.accelerider.com/v2/graph/accessToken?refeshToken=" + RefreshToken));
                AccessToken = json.Value<string>("accessToken");
            }
        }

        public override Task<ILazyTreeNode<INetDiskFile>> GetFileRootAsync()
        {
            var tree = new LazyTreeNode<INetDiskFile>(new OneDriveFile()
            {
                Owner = this
            })
            {
                ChildrenProvider = async parent =>
                {
                    var result = new List<OneDriveFile>();
                    if (parent.Path == "/")
                    {
                        result.AddRange((await Api.GetRootFilesAsync()).FileList);
                    }
                    else
                    {
                        var tmp = await Api.GetFilesAsync(parent.Path);
                        result.AddRange(tmp.FileList);
                        while (!string.IsNullOrEmpty(tmp.NextPage))
                        {
                            using (var client = new HttpClient()
                            {
                                DefaultRequestHeaders =
                                {
                                    Authorization = new AuthenticationHeaderValue("Bearer",AccessToken)
                                }
                            })
                            {
                                tmp = JsonConvert.DeserializeObject<OneDriveListFileResult>(await client.GetStringAsync(tmp.NextPage));
                                result.AddRange(tmp.FileList);
                            }
                        }
                    }

                    result.ForEach(v => v.Owner = this);
                    return result;
                }
            };
            return Task.FromResult((ILazyTreeNode<INetDiskFile>)tree);
        }

        public override TransferItem Download(ILazyTreeNode<INetDiskFile> @from, FileLocator to)
        {
            throw new NotImplementedException();
        }

        public override Task UploadAsync(FileLocator @from, INetDiskFile to, Action<TransferItem> callback)
        {
            throw new NotImplementedException();
        }

        protected override IDownloaderBuilder ConfigureDownloaderBuilder(IDownloaderBuilder builder)
        {
            throw new NotImplementedException();
        }

        protected override IRemotePathProvider GetRemotePathProvider(string jsonText)
        {
            throw new NotImplementedException();
        }
    }

    internal class AuthenticatedHttpClientHandler : HttpClientHandler
    {
        private readonly Func<string> _token;
        public AuthenticatedHttpClientHandler(Func<string> tokenAction)
        {
            _token = tokenAction;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_token != null)
                request.Headers.Authorization = new AuthenticationHeaderValue(request.Headers.Authorization.Scheme, _token.Invoke());
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

}

