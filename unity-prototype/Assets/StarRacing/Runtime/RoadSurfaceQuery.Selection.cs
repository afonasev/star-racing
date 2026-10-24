using System;
using System.Collections.Generic;
namespace StarRacingPrototype {
 public sealed partial class RoadSurfaceQuery {
  // Partition by the existing total order. The lower median set is exactly the
  // full-sort control's set. A bounded bad pivot sequence falls back to that sort.
  internal static void PartitionMedian<T>(T[] values,int start,int count,int middle,IComparer<T> order,int partitionBudget=-1){
   if(count<2)return;
   if(partitionBudget<0){int log=0;for(int n=count;n>1;n>>=1)log++;partitionBudget=2*log;}
   int low=start,high=start+count-1;
   while(high-low>16){
    if(partitionBudget--==0){Array.Sort(values,start,count,order);return;}
    T a=values[low],b=values[low+(high-low)/2],c=values[high];
    if(order.Compare(a,b)>0){T temp=a;a=b;b=temp;}
    if(order.Compare(b,c)>0){b=c;if(order.Compare(a,b)>0)b=a;}
    T pivot=b;int left=low,right=high;
    while(left<=right){
     while(order.Compare(values[left],pivot)<0)left++;
     while(order.Compare(values[right],pivot)>0)right--;
     if(left>right)break;
     T temp=values[left];values[left]=values[right];values[right]=temp;left++;right--;
    }
    if(middle<=right)high=right;else if(middle>=left)low=left;else return;
   }
   Array.Sort(values,low,high-low+1,order);
  }
 }
}
