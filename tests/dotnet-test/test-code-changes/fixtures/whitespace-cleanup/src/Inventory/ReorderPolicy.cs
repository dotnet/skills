namespace Inventory;

public static class ReorderPolicy
{
  public static int SuggestedOrder(int onHand,int reorderPoint,int targetLevel)
  {
      if (onHand > reorderPoint)
      {
          return 0;
      }

      return Math.Max(0, targetLevel-onHand);
  }
}