using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;

    LinkedList<PlayerAccount> playerAccounts;
    LinkedList<GameRoom> gameRooms;

    const int PlayerAccountNameAndPassword = 1;
    
    string playerAccountsFilePath;
    
    int playerWaitingForMatchWithID = -1;


    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        //Defines network topology for the host
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);


        playerAccountsFilePath = Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccounts.txt";
        playerAccounts = new LinkedList<PlayerAccount>();
        //READ IN PLAYER ACCOUNT 
        LoadPlayerAccounts();

        //foreach (PlayerAccount pa in playerAccounts)
        //    Debug.Log(pa.name + " " + pa.password);
        gameRooms = new LinkedList<GameRoom>();
    }

    // Update is called once per frame
    void Update()
    {

        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;
        //Used to poll the underlying systems for events
        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }

    }
  
    //Used to send messages to the client, could be things such as player locations or other information(i believe)
    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }

    //For Recieving messages
    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);

        string[] csv = msg.Split(',');
        int signifier = int.Parse(csv[0]);

        if (signifier == ClientToServerSignifiers.CreateAccount)
        {
            string n = csv[1];
            string p = csv[2];
            bool nameInUse = false;
            //check if player account name exists
            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n)
                {
                    nameInUse = true;
                    break;
                }
            }

            if (nameInUse)
            {
                SendMessageToClient(ServerToClientSignifiers.AccountCreationFailed + "", id);
            }
            else
            {
                //if not create new account, add to list, 
                PlayerAccount newPlayerAccount = new PlayerAccount(n, p);
                playerAccounts.AddLast(newPlayerAccount);
                SendMessageToClient(ServerToClientSignifiers.AccountCreationComplete + "", id);

                //save list to HD
                SavePlayerAccounts();
            }
        }
        else if (signifier == ClientToServerSignifiers.Login)
        {
            //check if player account name exisits,
            string n = csv[1];
            string p = csv[2];
            bool hasNameBeenFound = false;
            bool msgHasBeenSentToClient = false;

            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n)
                {
                    hasNameBeenFound = true;
                    if (pa.password == p)
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginComplete + "", id);
                        msgHasBeenSentToClient = true;
                    }
                    else
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginFailed + "", id);
                        msgHasBeenSentToClient = true;
                    }
                }

                if (!hasNameBeenFound && !msgHasBeenSentToClient)
                {
                    SendMessageToClient(ServerToClientSignifiers.LoginFailed + "", id);
                }
            }
        }
        else if (signifier == ClientToServerSignifiers.JoinGameRoomQueue)
        {
            Debug.Log("We need to get this player into a waiting queueue");
            //Check if the client is a player
            //if(ServerToClientSignifiers.ClientIsPlayer == int.Parse(msg))
            //{
            //SendMessageToClient("Client is player", id);
            if (playerWaitingForMatchWithID == -1)
            {
                playerWaitingForMatchWithID = id;
            }
            else
            {
                GameRoom gr = new GameRoom(playerWaitingForMatchWithID, id);
                gameRooms.AddLast(gr);
                Debug.Log("GameRoom Added");
                SendMessageToClient(ServerToClientSignifiers.PlayerO + "", gr.playerID2);
                SendMessageToClient(ServerToClientSignifiers.PlayerX + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifiers.GameStart + "", gr.playerID2);
                SendMessageToClient(ServerToClientSignifiers.GameStart + "", gr.playerID1);

                Debug.Log("GameStart message sent");
                playerWaitingForMatchWithID = -1;
            }
            //}
        }

        //If the client is an observer
        //    TO ADD : Disable anything the client as an observer can do
        else if (signifier == ClientToServerSignifiers.ClientIsObserver)
        {
            SendMessageToClient("Client is observer", id);

        } 
        else if (signifier == ClientToServerSignifiers.OpponentPlay)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            Debug.Log("OpponentPlay Called");
            if (gr != null)
            {
                if (gr.playerID1 == id)
                {
                    Debug.Log(csv[1] + "" + "," + csv[2] + "");
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "," + csv[1] + "," + csv[2], gr.playerID2);
                    //                                   Goes to OpponentPlay       Button Num  Player Side (X or O)
                }
                else
                {
                    Debug.Log(csv[1] + "" + "," + csv[2] + "");
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "," + csv[1] + "," + csv[2], gr.playerID1);
                }


            }
            //Get The game room that the client id is in

            //ONCE THE GAME ROOM IS MADE

        }

        else if (signifier == ClientToServerSignifiers.GameOver)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            string Winningplayer = csv[1];
            Debug.Log("Game Over Called");
            if (gr != null)
            {
                if (gr.playerID1 == id)
                {
                    
                    SendMessageToClient(ServerToClientSignifiers.GameOver + "," + csv[1], gr.playerID2);
                    
                }
                else
                {
                    Debug.Log(csv[1] + "" + "," + csv[2] + "");
                    SendMessageToClient(ServerToClientSignifiers.GameOver + "," + csv[1], gr.playerID1);
                }


            }
        }

    }


    private void SavePlayerAccounts()
    {
        StreamWriter SW = new StreamWriter(playerAccountsFilePath);

        foreach(PlayerAccount pa in playerAccounts)
        {
            SW.WriteLine(PlayerAccountNameAndPassword + "," + pa.name + "," + pa.password);
        }

        SW.Close();
    }

    private void LoadPlayerAccounts()
    {
        File.Exists(playerAccountsFilePath);
        
        StreamReader sr = new StreamReader(playerAccountsFilePath);

        string line;

        while((line = sr.ReadLine()) != null)
        {
            string[] csv = line.Split(',');

            int signifier = int.Parse(csv[0]);

            if(signifier == PlayerAccountNameAndPassword)
            {
                PlayerAccount pa = new PlayerAccount(csv[1], csv[2]);
                playerAccounts.AddLast(pa);
            }
        }

        sr.Close();
    }

    private GameRoom GetGameRoomWithClientID(int ID)
    {
        foreach(GameRoom gr in gameRooms)
        {
            if(gr.playerID1 == ID || gr.playerID2 == ID)
            {
                return gr;
            }
        }

        return null;
    }

}


public class PlayerAccount
{
    public string name, password;

    public PlayerAccount(string Name, string Password)
    {
        name = Name;
        password = Password;
    }
}


public class GameRoom
{
    public int playerID1, playerID2;

    public GameRoom(int PlayerID1, int PlayerID2) 
    {
        playerID1 = PlayerID1;
        playerID2 = PlayerID2;
    }
}



public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;
    public const int Login = 2;
    public const int JoinGameRoomQueue = 3;
    public const int TicTacToeSomethingPlay = 4;
    public const int ClientIsObserver = 5;
    public const int ClientIsPlayer = 6;
    public const int ClientWon = 7;
    public const int ClientLost = 8;
    public const int PlayerX = 9;
    public const int PlayerO = 10;
    public const int OpponentPlay = 11;
    public const int GameOver = 12;
}

public static class ServerToClientSignifiers
{
    public const int LoginComplete = 1;
    public const int LoginFailed = 2;
    public const int AccountCreationComplete = 3;
    public const int AccountCreationFailed = 4;
    public const int OpponentPlay = 5;
    public const int GameStart = 6;
    public const int ClientIsObserver = 7;
    public const int ClientIsPlayer = 8;
    public const int ClientWon = 9;
    public const int ClientLost = 10;
    public const int PlayerX = 11;
    public const int PlayerO = 12;
    public const int GameOver = 13;
}
