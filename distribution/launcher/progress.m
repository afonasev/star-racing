#import <AppKit/AppKit.h>
#include <stdio.h>
@interface ProgressDelegate : NSObject <NSApplicationDelegate,NSWindowDelegate>
@property NSWindow *window;
@property NSTextField *label;
@property NSProgressIndicator *bar;
@end
@implementation ProgressDelegate
- (void)applicationDidFinishLaunching:(NSNotification *)notification {
    self.window=[[NSWindow alloc] initWithContentRect:NSMakeRect(0,0,480,160) styleMask:NSWindowStyleMaskTitled|NSWindowStyleMaskClosable backing:NSBackingStoreBuffered defer:NO];
    self.window.title=@"Star Racing — обновление";self.window.delegate=self;
    self.label=[NSTextField labelWithString:@"Подготовка обновления…"];
    self.label.frame=NSMakeRect(24,103,432,30);[self.window.contentView addSubview:self.label];
    self.bar=[[NSProgressIndicator alloc] initWithFrame:NSMakeRect(24,68,432,20)];
    self.bar.indeterminate=NO;self.bar.minValue=0;self.bar.maxValue=100;[self.window.contentView addSubview:self.bar];
    NSButton *cancel=[NSButton buttonWithTitle:@"Отменить и играть" target:self action:@selector(cancel:)];
    cancel.frame=NSMakeRect(260,16,196,32);[self.window.contentView addSubview:cancel];
    NSString *snapshot=NSProcessInfo.processInfo.environment[@"STAR_RACING_PROGRESS_SNAPSHOT"];
    if(snapshot.length) {
        self.label.stringValue=@"Загрузка обновления · 67%";self.bar.doubleValue=67;
        [self.window display];
        dispatch_async(dispatch_get_main_queue(),^{
            NSView *view=self.window.contentView.superview;
            NSBitmapImageRep *bitmap=[view bitmapImageRepForCachingDisplayInRect:view.bounds];
            [view cacheDisplayInRect:view.bounds toBitmapImageRep:bitmap];
            [[bitmap representationUsingType:NSBitmapImageFileTypePNG properties:@{}] writeToFile:snapshot atomically:YES];
            [NSApp terminate:nil];
        });
        return;
    }
    [self.window center];[self.window makeKeyAndOrderFront:nil];[NSApp activateIgnoringOtherApps:YES];
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_UTILITY,0),^{
        char *line=NULL;size_t capacity=0;
        while(getline(&line,&capacity,stdin)>0) {
            char *separator=strchr(line,'\t');if(!separator)continue;*separator=0;
            double percent=atof(line);NSString *message=[[NSString stringWithUTF8String:separator+1] stringByTrimmingCharactersInSet:NSCharacterSet.newlineCharacterSet];
            dispatch_async(dispatch_get_main_queue(),^{self.label.stringValue=[NSString stringWithFormat:@"%@ · %.0f%%",message,percent];self.bar.doubleValue=percent;});
        }
        free(line);dispatch_async(dispatch_get_main_queue(),^{[NSApp terminate:nil];});
    });
}
- (void)cancel:(id)sender { [NSApp terminate:nil]; }
- (BOOL)windowShouldClose:(NSWindow *)sender { [NSApp terminate:nil];return YES; }
@end
int main(void) { @autoreleasepool { NSApplication *app=NSApplication.sharedApplication;ProgressDelegate *delegate=[ProgressDelegate new];app.delegate=delegate;[app setActivationPolicy:NSApplicationActivationPolicyAccessory];[app run]; }return 0; }
