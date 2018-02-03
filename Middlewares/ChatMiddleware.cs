using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
namespace WebsocketsChatDemo.Middlewares {
public class ChatMiddleware
{
  private readonly RequestDelegate next;
  private readonly ConcurrentDictionary<WebSocket, Guid> connectedClients;
  public ChatMiddleware(RequestDelegate next)
  {
    //In questo dizionario manterremo la lista dei client connessi
	//Usiamo un ConcurrentDictionary per gestire la lista in maniera thread-safe
    connectedClients = new ConcurrentDictionary<WebSocket, Guid>();
    this.next = next;
  }
  public async Task InvokeAsync(HttpContext context)
  {
    //Se non si tratta di una richiesta WebSockets, continuiamo come al solito,
	//lasciando che la richiesta venga gestita dal middleware successivo
    if (!context.WebSockets.IsWebSocketRequest)
    {
      await next.Invoke(context);
      return;
    }
	//Altrimenti la facciamo gestire al ChatMiddleware
    WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
    await HandleWebSocketCommunication(context, webSocket);
  }

  public async Task HandleWebSocketCommunication(HttpContext context, WebSocket webSocket)
  {
    //Aggiungiamo questo webSocket alla lista dei client connessi
    //Forniamo anche un Guid che potrebbe essere usato come riferimento a questo client
    //in scenari avanzati, come ad esempio l'invio di messaggi privati 
    connectedClients.TryAdd(webSocket, Guid.NewGuid());

    WebSocketReceiveResult result;
    do
    {
      //Iniziamo a ricevere messaggi dal client
      //Il buffer deve essere sufficientemente grande per accomodare l'intero messaggio
      var buffer = new byte[4 * 1024];
      result = await webSocket.ReceiveAsync(
        buffer: new ArraySegment<byte>(buffer),
        cancellationToken: CancellationToken.None);

      //Convertiamo il messaggio binario in stringa
      var message = Encoding.UTF8.GetString(buffer).Trim(' ', '\0');
      //E lo inoltriamo a tutti i client
      await SendMessageToClients(message);
      //TODO: Qui potremmo storicizzare il messaggio in un database
    
    //Ripetiamo finché il client non si disconnette
    } while (!result.CloseStatus.HasValue);

	//Quando il client risulta disconnesso, lo rimuoviamo dall'elenco e chiudiamo il socket
    connectedClients.TryRemove(webSocket, out _);
    await webSocket.CloseAsync(
      closeStatus: result.CloseStatus.Value,
      statusDescription: result.CloseStatusDescription,
      cancellationToken: CancellationToken.None);
  }

  private async Task SendMessageToClients(string message)
  {
    foreach (var socket in connectedClients.Keys)
    {
      var responseBuffer = Encoding.UTF8.GetBytes(message);
      await socket.SendAsync(
        buffer: new ArraySegment<byte>(responseBuffer, 0, responseBuffer.Length),
        messageType: WebSocketMessageType.Text,
        endOfMessage: true,
        cancellationToken: CancellationToken.None);
    }
  }
}
}