using System;
using IO;
using Utilities;
using UnityEngine;

public class ChessPieceScript : MonoBehaviour, ISerializable<ChessPieceData>
{
    public int Row { get; set; }
    public int Column { get; set; }

    public string Type { get; set; }
    public bool White { get; set; }
    public bool Alive { get; set; }
    public uint MovesAmount { get; set; } = 0;

    public Texture2D texture2D;
    
    public bool CanEat(ChessPieceScript chessPiece)
    {
        return chessPiece != null && Movement.IsInRange(Type, (Row, Column), (chessPiece.Row, chessPiece.Column))&& White ? !chessPiece.White : chessPiece.White;
    }
    
    public bool CanEat((int, int) cord)
    {
        try
        {
            var chessPiece = GameManagerScript.PiecesOnBoard[cord];
            return Movement.IsInRange(Type, (Row, Column), (chessPiece.Row, chessPiece.Column)) && White
                ? !chessPiece.White
                : chessPiece.White;
        }
        catch
        {
            return false;
        }
    }

    public void LoadData(ChessPieceData data)
    {
        Row = data.Row;
        Column = data.Column;
        MovesAmount = data.MovesAmount;
        White = data.White;
        Alive = data.Alive;
        Type = data.Type;

        transform.position = GameManagerScript.Utilities.GetPosition(Row, Column);
        var pieceSpriteRenderer = GetComponent<SpriteRenderer>();
        
        if (Type.Equals(PieceTypes.Pawn.Name))
        {
            pieceSpriteRenderer.sprite = Sprite.Create(texture2D, PieceTypes.SerializeType(Type).GetValueOrDefault(PieceTypes.Pawn).TextureRect,
                new Vector2(-0.25f, 0), 60f);
        }
        else
        {
            pieceSpriteRenderer.sprite = Sprite.Create(texture2D, PieceTypes.SerializeType(Type).GetValueOrDefault(PieceTypes.Pawn).TextureRect,
                new Vector2(-0.1f, 0), 60f);
        }

        pieceSpriteRenderer.color = White ? Color.white : Color.black;
        
        if (!Alive)
        {
            GameObject.Find("DeadPileManager").GetComponent<DeadPileManagerScript>().AddToDeadPile(this);
        }
    }

    public ChessPieceData SaveData()
    {
        return ChessPieceData.FromReal(this);
    }
}
