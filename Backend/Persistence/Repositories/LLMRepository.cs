using Domain.Persistence.DTOs;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using Domain.Persistence.Configuration;

namespace Persistence.Repositories;

#region Repository
public interface ILLMRepository
{
    Task<float[]?> ComputeEmbedding(
        string text, 
        CancellationToken cancellationToken = default
    );

    Task<QueryAnswer> QueryLLM(
        string document_id, 
        string question, 
        CancellationToken cancellationToken = default
    );

    Task<IEnumerable<float[]?>> ComputeBatchEmbeddings(
        IEnumerable<string> texts, 
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Repository for interfacing with the LLM service
/// This repository is responsible for querying the LLM model
/// and computing the embeddings of text
/// It uses the WebRepository to make HTTP requests to the LLM service
/// The LLM service is a REST API which provides endpoints for querying the model
/// and computing the embeddings of text
/// The LLM service is a separate service which is responsible for running the LLM model (in python server)
/// </summary>
/// <param name="options">
///     Options for the LLM service
/// </param>
/// <param name="queryService">
///     Web repository for querying the LLM model - specifically the /query endpoint
/// </param>
/// <param name="computeService">
///     Web repository for computing the embeddings of text - specifically the /compute_embedding endpoint
/// </param>
public class LLMRepository(
    ILLMServiceLoadBalancer _llmServiceLoadBalancer,
    IWebRepository<float[]?> _computeService, 
    IWebRepository<IEnumerable<float[]?>> _batchComputeService, 
    IWebRepository<QueryAnswer> _queryService
) : ILLMRepository
{
    #region Service URLs
    private string QUERY_URL => _llmServiceLoadBalancer.GetServiceUrl() + "/query";
    private string COMPUTE_URL => _llmServiceLoadBalancer.GetServiceUrl() + "/compute_embedding";
    private string COMPUTE_BATCH_URL => _llmServiceLoadBalancer.GetServiceUrl() + "/compute_batch_embedding";
    #endregion

    #region Public Methods
    /// <summary>
    /// Query the LLM model with a given document_id and question
    /// Calls the /query endpoint of the LLM service
    /// Returns the answer to the question as interpreted by the LLM model
    /// Given the question provided, the LLM will use the most relevant rows 
    /// from the excel sheet to answer the question
    /// </summary>
    /// <param name="document_id">
    ///     The document_id of the excel sheet which contains the data
    /// </param>
    /// <param name="question">
    ///     The question to ask the LLM model
    /// </param>
    /// <returns>
    ///     QueryAnswer. The answer to the question as interpreted by the LLM model
    /// </returns>
    public async Task<QueryAnswer> QueryLLM(string document_id, string question, CancellationToken cancellationToken = default) => 
        await _queryService.PostAsync(
            QUERY_URL, 
            new 
            {
                question,
                document_id
            },
            cancellationToken
        );

    /// <summary>
    /// Compute the embedding of a given text
    /// Calls the /compute_embedding endpoint of the LLM service
    /// Returns a vector which represents the text or data as interpreted by the LLM model
    /// </summary>
    /// <param name="document_id"></param>
    /// <param name="text"></param>
    /// <returns>
    /// Vector representing the text or data
    /// </returns>
    public async Task<float[]?> ComputeEmbedding(string text, CancellationToken cancellationToken = default) => 
        await _computeService.PostAsync(
            COMPUTE_URL, 
            new 
            { 
                text 
            }, 
            cancellationToken
        );

    /// <summary>
    /// Compute the embeddings of a batch of texts
    /// Calls the /compute_embedding endpoint of the LLM service with many
    /// texts at once to reduce repetitive calls, batch up calls
    /// </summary>
    /// <param name="texts"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<IEnumerable<float[]?>> ComputeBatchEmbeddings(IEnumerable<string> texts, CancellationToken cancellationToken = default) => 
        await _batchComputeService.PostAsync(
            COMPUTE_BATCH_URL, 
            new 
            { 
                texts = texts.ToList()     
            }, 
            cancellationToken
        );
    #endregion
}
#endregion

#region Load Balancer
public interface ILLMServiceLoadBalancer
{
    string GetServiceUrl();
}

[ExcludeFromCodeCoverage]
public class LLMLoadBalancer(IOptions<LLMServiceOptions> options) : ILLMServiceLoadBalancer
{
    #region Private Fields
    private int _currentIndex = 0;
    private readonly object _lock = new();
    private readonly string _serviceUrl = options.Value.LLM_SERVICE_URL;
    private readonly List<string> _serviceUrls = options.Value.LLM_SERVICE_URLS;
    private readonly bool _weAreInSwarmMode = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_SWARM_TASK_ID"));
    #endregion

    public string GetServiceUrl()
    {
        // If we are in swarm mode, it handles load balancing for us
        if (_weAreInSwarmMode)
        {
            return _serviceUrl;
        }
        else
        {
            lock (_lock)
            {
                if (_currentIndex >= _serviceUrls.Count) _currentIndex = 0;
                return _serviceUrls[_currentIndex++];
            }
        }
    }
}
#endregion