using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;  // For `MemoryStream()`
using System.Linq;  // For `ToList()`

using UnityEngine;
using UnityEngine.UI;

using Niantic.ARDK.AR.HitTest;
using Niantic.ARDK.Extensions;
using Niantic.ARDK.Networking;
using Niantic.ARDK.Networking.MultipeerNetworkingEventArgs;
using Niantic.ARDK.Utilities;
using Niantic.ARDK.Utilities.BinarySerialization;
using Niantic.ARDK.Utilities.Input.Legacy;

public class GomokuGameManagerScript : MonoBehaviour
{
    public Camera camera;
    public ARNetworkingManager manager;
    public GameObject boardObject;
    public GameObject blackStonePrefab;
    public GameObject whiteStonePrefab;
    public Text hostOrGuestText;
    public Text currentStateText;

    private bool isBoardObjectInitialized = false;
    private int boardSquareNum;
    private int[,] boardSquares;

    private const int EMPTY = -1;
    private const int BLACK = 0;
    private const int WHITE = 1;
    private Dictionary<int, string> colorStringMapper = new Dictionary<int, string>()
    {
        {EMPTY, "Empty"},
        {BLACK, "Black"},
        {WHITE, "White"}
    };

    // TODO: Manages the current turn by network messaging.
    private int currentTurnColor = BLACK;

    // TODO: Make it able to reset the game after winner is determined.
    private int winnerColor = EMPTY;

    private const uint TAG_INITIALIZE_BOARD = 0;
    private const uint TAG_PUT_STONE = 1;

    private const int WIN_SQUARE_NUM = 3;

    // Start is called before the first frame update.
    IEnumerator Start()
    {
        yield return new WaitForSeconds(0.1f);
        manager.NetworkSessionManager.Networking.Connected += OnNetworkInitialized;
    }

    // Network initialized event.
    void OnNetworkInitialized(ConnectedArgs args)
    {
        manager.NetworkSessionManager.Networking.PeerDataReceived += OnPeerDataReceived;

        // Moves `boardObject` to somewhere else in order to not be shown in the screen at first.
        boardObject.transform.localPosition = new Vector3(
            999.0f,
            999.0f,
            999.0f
        );

        // Moves `hostOrGuestText` to the top-center of the screen.
        hostOrGuestText.transform.localPosition = new Vector3(
            0.0f,
            Screen.height * 0.4f,
            0.0f
        );
        hostOrGuestText.text = AmIHost() ? "[Host]" : "[Guest]";

        // Moves `currentStateText` to the top-center of the screen.
        currentStateText.transform.localPosition = new Vector3(
            0.0f,
            Screen.height * 0.3f,
            0.0f
        );
        currentStateText.text = "Please set the board";
    }

    bool AmIHost()
    {
        return (manager.NetworkSessionManager.Networking.Self.Identifier ==
                manager.NetworkSessionManager.Networking.Host.Identifier);
    }

    // Peer data received event.
    void OnPeerDataReceived(PeerDataReceivedArgs args)
    {
        MemoryStream stream = new MemoryStream(args.CopyData());
        switch (args.Tag)
        {
            case TAG_INITIALIZE_BOARD:
                InitializeBoard(hitTestPosition: (Vector3)GlobalSerializer.Deserialize(stream));
                currentStateText.text = "Current turn: " + colorStringMapper[currentTurnColor];
                break;

            case TAG_PUT_STONE:
                PutStone(hitTestPosition: (Vector3)GlobalSerializer.Deserialize(stream));
                currentStateText.text = "Current turn: " + colorStringMapper[currentTurnColor];
                winnerColor = CheckWinnerColorFourDirections();
                if (winnerColor == BLACK || winnerColor == WHITE)
                {
                    currentStateText.text = "Winner: " + colorStringMapper[winnerColor];
                }
                break;

            default:
                break;
        }
    }

    void InitializeBoard(Vector3 hitTestPosition)
    {
        boardObject.transform.position = hitTestPosition;
        boardObject.transform.rotation = Quaternion.identity;

        boardSquareNum = (int)Math.Sqrt(boardObject.transform.childCount);
        boardSquares = new int[boardSquareNum, boardSquareNum];
        for (int h = 0; h < boardSquareNum; ++h)
        {
            for (int w = 0; w < boardSquareNum; ++w)
            {
                boardSquares[h, w] = EMPTY;
            }
        }

        isBoardObjectInitialized = true;
    }

    void PutStone(Vector3 hitTestPosition)
    {
        float boardSquareScale = boardObject.transform.GetChild(0).gameObject.transform.localScale.x;
        Vector3 boardTopLeftCornerPosition = boardObject.transform.position + new Vector3(
            -boardSquareScale * 0.5f,
            0.0f,
            boardSquareScale * 0.5f
        );

        int h = (int)(-(hitTestPosition.z - boardTopLeftCornerPosition.z) / boardSquareScale);
        int w = (int)((hitTestPosition.x - boardTopLeftCornerPosition.x) / boardSquareScale);
        Debug.Log("h = " + h + ", w = " + w);

        if (0 <= h && h < boardSquareNum && 0 <= w && w < boardSquareNum &&
            boardSquares[h, w] == EMPTY)
        {
            if (currentTurnColor == BLACK)
            {
                boardSquares[h, w] = BLACK;

                GameObject blackStone = Instantiate(blackStonePrefab, this.transform);
                blackStone.transform.position = new Vector3(
                    boardObject.transform.position.x + (w * boardSquareScale),
                    boardObject.transform.position.y + boardSquareScale,
                    boardObject.transform.position.z + (-h * boardSquareScale)
                );

                currentTurnColor = WHITE;
            }
            else if (currentTurnColor == WHITE)
            {
                boardSquares[h, w] = WHITE;

                GameObject whiteStone = Instantiate(whiteStonePrefab, this.transform);
                whiteStone.transform.position = new Vector3(
                    boardObject.transform.position.x + (w * boardSquareScale),
                    boardObject.transform.position.y + boardSquareScale,
                    boardObject.transform.position.z + (-h * boardSquareScale)
                );

                currentTurnColor = BLACK;
            }
        }
    }

    // Update is called once per frame.
    void Update()
    {
        if (winnerColor == BLACK || winnerColor == WHITE)
        {
            return;
        }

        if (PlatformAgnosticInput.touchCount <= 0)
        {
            return;
        }

        var touch = PlatformAgnosticInput.GetTouch(0);
        if (touch.phase == TouchPhase.Began)
        {
            OnTouchBegan(touch);
        }
    }

    void OnTouchBegan(Touch touch)
    {
        var currentFrame = manager.ARSessionManager.ARSession.CurrentFrame;
        if (currentFrame == null)
        {
            return;
        }

        var hitTestResults = currentFrame.HitTest(
            camera.pixelWidth,
            camera.pixelHeight,
            touch.position,
            ARHitTestResultType.EstimatedHorizontalPlane
        );
        if (hitTestResults.Count <= 0)
        {
            return;
        }

        Vector3 hitTestPosition = hitTestResults[0].WorldTransform.ToPosition();
        MemoryStream stream = new MemoryStream();
        GlobalSerializer.Serialize(stream, hitTestPosition);

        manager.ARNetworking.Networking.BroadcastData(
            tag: (!isBoardObjectInitialized) ? TAG_INITIALIZE_BOARD : TAG_PUT_STONE,
            data: stream.ToArray(),
            transportType: TransportType.ReliableOrdered,
            sendToSelf: true
        );
    }

    int CheckWinnerColorFourDirections()
    {
        for (int h = 0; h < boardSquareNum; ++h)
        {
            for (int w = 0; w < boardSquareNum; ++w)
            {
                int color = boardSquares[h, w];
                if (color == EMPTY)
                {
                    continue;
                }

                // 1: Bottom.
                if (CheckWinnerColorOneDirection(color, h, w, h_direction: 1, w_direction: 0))
                {
                    return color;
                }

                // 2: Right.
                if (CheckWinnerColorOneDirection(color, h, w, h_direction: 0, w_direction: 1))
                {
                    return color;
                }

                // 3: Bottom-Right.
                if (CheckWinnerColorOneDirection(color, h, w, h_direction: 1, w_direction: 1))
                {
                    return color;
                }

                // 4: Bottom-Left.
                if (CheckWinnerColorOneDirection(color, h, w, h_direction: 1, w_direction: -1))
                {
                    return color;
                }
            }
        }

        return EMPTY;
    }

    bool CheckWinnerColorOneDirection(int color, int h, int w, int h_direction, int w_direction)
    {
        int count = 0;

        for (int i = 0; i < WIN_SQUARE_NUM; ++i)
        {
            int delta_h = h_direction * i;
            int delta_w = w_direction * i;

            if (0 <= h + delta_h && h + delta_h < boardSquareNum && 0 <= w + delta_w && w + delta_w < boardSquareNum &&
                boardSquares[h + delta_h, w + delta_w] == color)
            {
                count++;
            }
            else
            {
                break;
            }
        }

        return (count == WIN_SQUARE_NUM);
    }
}

