using System;
using System.Collections.Generic;
using IO;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Utilities;

public class GameManagerScript : MonoBehaviour
{
    public static bool UseAI = false;

    public static readonly Dictionary<(int, int), ChessPieceScript> PiecesOnBoard =
        new Dictionary<(int, int), ChessPieceScript>();

    public static readonly Dictionary<(int, int), CellScript> CellsOnBoard = new Dictionary<(int, int), CellScript>();

    private static Dictionary<ChessPieceScript, (float, float)> _moveQueue =
        new Dictionary<ChessPieceScript, (float, float)>();

    private static Dictionary<ChessPieceScript, (float, float)> _moveQueueCached;

    public float time;
    [FormerlySerializedAs("text")] public Text roundText;
    public Text timeText;
    public GameObject piecePrefab;
    [FormerlySerializedAs("cellObject")] public GameObject cellPrefab;
    public GameObject deadPileManager;
    public Texture2D overlayTexture;
    [FormerlySerializedAs("_round")] public uint round = 1;
    public GameObject escapeUI;
    public GameObject winUI;
    public AudioSource audioSource;

    private CellScript _selectedCell;
    private (int, int)? _whiteKingPosition = null;
    private (int, int)? _blackKingPosition = null;

    public delegate void OnMove(ChessPieceScript pieceToMove);

    public static event OnMove ONMoveEvent;

    void Start()
    {
        // reset in case player is re-entering scene from main menu after exit
        CellsOnBoard.Clear();
        PiecesOnBoard.Clear();
        // set escape ui to not shown by default
        escapeUI.SetActive(false);
        // set win ui to not shown by default
        winUI.SetActive(false);
        // initialize board
        InitializeBoard(Resources.Load<TextAsset>("starting_pos").text);
        roundText.text = "Round: " + round;
    }

    private void Update()
    {
        {
            if (_whiteKingPosition.HasValue)
            {
                if (_whiteKingPosition == (-1, -1))
                {
                    winUI.GetComponentInChildren<Text>().text = "Black Team Won!";
                    winUI.SetActive(true);
                }
            }
            
            if (_blackKingPosition.HasValue)
            {
                if (_blackKingPosition == (-1, -1))
                {
                    winUI.GetComponentInChildren<Text>().text = "White Team Won!";
                    winUI.SetActive(true);
                }
            }
        }
        {
            // show escape gui when escape key is called
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                escapeUI.SetActive(!escapeUI.activeSelf);
            }
        }
        {
            // increase time
            time += Time.deltaTime;

            float minutes = Mathf.FloorToInt(time / 60);
            float seconds = Mathf.FloorToInt(time % 60);

            timeText.text = "Time: " + string.Format("{0:00}:{1:00}", minutes, seconds);
        }
        {
            // move animation
            _moveQueueCached = new Dictionary<ChessPieceScript, (float, float)>(_moveQueue);
            foreach (var toMove in _moveQueue)
            {
                var obj = toMove.Key.gameObject;
                var vec3 = obj.transform.position;
                vec3 = new Vector3(math.lerp(vec3.x, toMove.Value.Item1, 0.1f),
                    math.lerp(vec3.y, toMove.Value.Item2, 0.1f), -0.1f);
                obj.transform.position = vec3;

                if (Math.Round(vec3.x, 5) == Math.Round(toMove.Value.Item1, 5) &&
                    Math.Round(vec3.y, 5) == Math.Round(toMove.Value.Item2, 5))
                {
                    _moveQueueCached.Remove(toMove.Key);
                }
            }

            _moveQueue = new Dictionary<ChessPieceScript, (float, float)>(_moveQueueCached);
        }
    }


    public void SelectCell(CellScript cell)
    {
        if (escapeUI.activeSelf) return;
        if (winUI.activeSelf) return;
        // if there is already a selected cell, plan to move selected piece on selected cell to newly clicked cell
        if (_selectedCell != null)
        {
            var movement = new PieceMovement(_selectedCell.ChessOnTop.Type, (cell.Row, cell.Column),
                (_selectedCell.Row, _selectedCell.Column));

            var selectedCellSpriteRenderer = _selectedCell.gameObject.GetComponent<SpriteRenderer>();
            selectedCellSpriteRenderer.sprite = null;

            foreach (var move in movement.GetPossibleMovements(_selectedCell.ChessOnTop))
            {
                var spriteRenderer = CellsOnBoard[move.GetDestCell()].gameObject.GetComponent<SpriteRenderer>();
                spriteRenderer.sprite = null;
            }

            if (cell.ChessOnTop == null)
            {
                if (Move(movement, false))
                {
                    cell.ChessOnTop = _selectedCell.ChessOnTop;
                    _selectedCell.ChessOnTop = null;
                    round++;
                    roundText.text = "Round: " + round;
                }
            }
            else if (_selectedCell.ChessOnTop.CanEat(cell.ChessOnTop))
            {
                if (Move(movement, true))
                {
                    cell.ChessOnTop = _selectedCell.ChessOnTop;
                    _selectedCell.ChessOnTop = null;
                    round++;
                    roundText.text = "Round: " + round;
                }
            }

            _selectedCell = null;
        }
        else
        {
            if (cell.ChessOnTop == null) return;

            // check if it is your turn to move the chess piece
            var pieceOnCell = cell.ChessOnTop;
            if (round % 2 != 0)
                if (!pieceOnCell.White)
                    return;

            if (round % 2 == 0)
                if (pieceOnCell.White)
                    return;
            _selectedCell = cell;

            // highlight selected cell
            var selectedCellSpriteRenderer = _selectedCell.gameObject.GetComponent<SpriteRenderer>();
            selectedCellSpriteRenderer.sprite =
                Sprite.Create(overlayTexture, new Rect(0, 0, 100, 100), new Vector2(0f, 0f));
            selectedCellSpriteRenderer.color = new Color(Color.cyan.r, Color.cyan.g, Color.cyan.b, 0.2f);

            var movement = new PieceMovement(_selectedCell.ChessOnTop.Type, (cell.Row, cell.Column),
                (_selectedCell.Row, _selectedCell.Column));

            // highlight possible positions to move to
            foreach (var move in movement.GetPossibleMovements(cell.ChessOnTop))
            {
                var spriteRenderer = CellsOnBoard[move.GetDestCell()].gameObject.GetComponent<SpriteRenderer>();
                spriteRenderer.sprite =
                    Sprite.Create(overlayTexture, new Rect(0, 0, 100, 100), new Vector2(0f, 0f));
                spriteRenderer.color = new Color(Color.green.r, Color.green.g, Color.green.b, 0.3f);
            }
        }
    }

    public bool Move(IMovement movement, bool eat)
    {
        ChessPieceScript piece;
        try
        {
            piece = PiecesOnBoard[movement.GetCurrentCell()];
        }
        catch
        {
            throw new Exception("Can not enqueue movement for piece as piece does not exist in data table");
        }

        var customMovement = movement.GetPossibleMovements(piece);
        if (customMovement == null) return false;
        foreach (var move in customMovement)
        {
            if (movement.GetDestCell() == move.GetDestCell())
            {
                return RawMove(ref piece, move, eat);
            }
        }

        return false;
    }

    private bool RawMove(ref ChessPieceScript piece, IMovement movement, bool eat)
    {
        if (eat)
        {
            try
            {
                var toBeEatenPiece = PiecesOnBoard[movement.GetDestCell()];
                PiecesOnBoard.Remove((toBeEatenPiece.Row, toBeEatenPiece.Column));
                deadPileManager.GetComponent<DeadPileManagerScript>().AddToDeadPile(toBeEatenPiece);
                if (toBeEatenPiece.Type.Equals("King"))
                {
                    if (toBeEatenPiece.White)
                    {
                        _whiteKingPosition = (-1, -1);
                    }
                    else
                    {
                        _blackKingPosition = (-1, -1);
                    }
                }
            }
            catch
            {
                Debug.LogError("Scheduled to eat piece at " + movement.GetDestCell().ToString() +
                               "but no piece found");
                throw new Exception();
            }
        }

        Utilities.QueueMove(piece, movement);
        PiecesOnBoard.Remove((piece.Row, piece.Column));
        piece.Row = movement.GetDestCell().Item1;
        piece.Column = movement.GetDestCell().Item2;
        piece.MovesAmount += 1;
        PiecesOnBoard.Add((piece.Row, piece.Column), piece);
        IOUtilities.Save();
        audioSource.Play();

        if (piece.Type.Equals("King"))
        {
            if (piece.White)
            {
                _whiteKingPosition = (piece.Row, piece.Column);
            }
            else
            {
                _blackKingPosition = (piece.Row, piece.Column);
            }
        }

        ONMoveEvent?.Invoke(piece);

        return true;
    }

    private void InitializeBoard(string json)
    {
        Debug.Log("Initializing board...");

        // initialize cells
        for (var row = 0; row < 8; row++)
        {
            for (var column = 0; column < 8; column++)
            {
                var createdCell = Instantiate(cellPrefab);
                createdCell.transform.position = Utilities.GetPosition(row, column);
                var script = createdCell.GetComponent<CellScript>();
                script.Row = row;
                script.Column = column;
                try
                {
                    script.ChessOnTop = PiecesOnBoard[(row, column)];
                }
                catch
                {
                    script.ChessOnTop = null;
                }

                CellsOnBoard.Add((row, column), script);
            }
        }

        var save = IOUtilities.Load();

        if (save == null)
        {
            var starting = IOUtilities.Load(json);
            LoadJson(starting);
        }
        else
        {
            LoadJson(save);
        }

        IOUtilities.Save();

        void LoadJson(Data data)
        {
            foreach (var piece in data.AlivePieces)
            {
                var createdPiece = Instantiate(piecePrefab);
                var script = createdPiece.GetComponent<ChessPieceScript>();
                script.LoadData(piece);
                PiecesOnBoard.Add((piece.Row, piece.Column), script);
                CellsOnBoard[(piece.Row, piece.Column)].ChessOnTop = script;
                if (!script.Type.Equals("King")) continue;
                if (script.White)
                {
                    _whiteKingPosition = (script.Row, script.Column);
                }
                else
                {
                    _blackKingPosition = (script.Row, script.Column);
                }
            }

            foreach (var dead in data.DeadPieces)
            {
                var createdPiece = Instantiate(piecePrefab);
                var script = createdPiece.GetComponent<ChessPieceScript>();
                script.LoadData(dead);
                deadPileManager.GetComponent<DeadPileManagerScript>().AddToDeadPile(script, false);
                if (script.White)
                {
                    _whiteKingPosition = (-1, -1);
                }
                else
                {
                    _blackKingPosition = (-1, -1);
                }
            }
        }
    }

    public static class Utilities
    {
        public static Vector3 GetPosition(int row, int column)
        {
            return new Vector3(-4f + row, 3f - column, -0.1f);
        }

        public static void QueueMove(ChessPieceScript chessPieceScript, IMovement movement)
        {
            var a = GetPosition(movement.GetDestCell().Item1, movement.GetDestCell().Item2);
            _moveQueue.Add(chessPieceScript, (a.x, a.y));
        }

        public static void QueueMove(ChessPieceScript chessPieceScript, Vector3 movement)
        {
            _moveQueue.Add(chessPieceScript, (movement.x, movement.y));
        }
    }
}