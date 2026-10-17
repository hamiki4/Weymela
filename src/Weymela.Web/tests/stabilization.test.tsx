import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { invalidateResourceCache } from "../src/api/client";
import { PayoutDestinationForm } from "../src/features/PayoutDestinationForm";
import { Inbox } from "../src/features/notifications/Inbox";
import { PromotionManagement, type BusinessPromotionItem } from "../src/features/business/PromotionManagement";
import { DisplayedNotifications } from "../src/features/notifications/DisplayedNotifications";
import { mockApi } from "./fixtures";

beforeEach(()=>invalidateResourceCache(false));
it("opening one notification acknowledges it before displaying its destination",async()=>{
 const api=mockApi({"/notifications":{unreadCount:2,items:[
  {id:"first",title:"Your work",message:"Review your work",route:"/creator/promotions",createdAtUtc:"2026-10-14T00:00:00Z",readAtUtc:null},
  {id:"second",title:"Another item",message:"Another item",route:"/creator/earnings",createdAtUtc:"2026-10-14T00:00:00Z",readAtUtc:null}]}});
 render(<MemoryRouter initialEntries={["/notifications"]}><Routes><Route path="/notifications" element={<Inbox/>}/><Route path="/creator/promotions" element={<h1>Displayed work</h1>}/></Routes></MemoryRouter>);
 await screen.findByText("Your work");await userEvent.click(screen.getAllByRole("link",{name:"Your work"})[0]);
 expect(await screen.findByRole("heading",{name:"Displayed work"})).toBeVisible();
 expect(api.writes.map(write=>write.path)).toEqual(["/notifications/first/read"]);
});
it("Business tracking separates review, active work, and completed deliverables with distinct actions",async()=>{
 const base:BusinessPromotionItem={key:"review",id:"review",campaignId:"ugc",kind:"UGC deliverable",title:"Review content",group:"Pending",status:"Pending Business review",budget:4900,remaining:4900,dueAtUtc:"2026-10-20T00:00:00Z",primaryAction:"Review video",actionPath:"/business/ugc/ugc",creator:"Creator",remainingDays:null,creatorCount:1,lowFunds:false};
 mockApi({"/business/promotion-management":[base,{...base,key:"active",id:"active",title:"Awaiting publication",group:"Active",status:"Approved for publication",primaryAction:"View deliverable"},{...base,key:"done",id:"done",title:"Published work",group:"Completed",status:"Published · fixed fee earned",remaining:0}]});
 render(<MemoryRouter><PromotionManagement/></MemoryRouter>);
 await screen.findByRole("heading",{name:"Review content"});expect(screen.queryByText("Remaining")).not.toBeInTheDocument();expect(screen.getByRole("link",{name:"Review video"})).toHaveAttribute("href","/business/ugc/ugc");
 await userEvent.click(screen.getByRole("tab",{name:"Active (1)"}));
 expect(screen.getByRole("heading",{name:"Awaiting publication"})).toBeVisible();expect(screen.queryByText("Review content")).not.toBeInTheDocument();
 await userEvent.click(screen.getByRole("tab",{name:"Completed (1)"}));expect(screen.getByRole("heading",{name:"Published work"})).toBeVisible();
 await waitFor(()=>expect(screen.getByRole("tab",{name:"Pending (1)"})).toHaveAttribute("aria-selected","false"));
});

afterEach(()=>{vi.unstubAllGlobals();window.sessionStorage.removeItem("weymela.profile-key");});
it("only visible, loaded records acknowledge notifications and stale profile responses cannot mix counts",async()=>{
 let changed:IntersectionObserverCallback=()=>{};const observed:Element[]=[];
 class Observer { constructor(callback:IntersectionObserverCallback){changed=callback;} observe(element:Element){observed.push(element);} unobserve(){} disconnect(){} }
 vi.stubGlobal("IntersectionObserver",Observer);
 const api=mockApi({});window.sessionStorage.setItem("weymela.profile-key","Customer:one");
 const id="11111111-2222-4333-8444-555555555555";
 const {container,rerender}=render(<><DisplayedNotifications profileKey="Customer:one"/><div aria-busy="true"><article data-notification-resource={id}>Loading cached record</article></div></>);
 expect(observed).toHaveLength(0);
 rerender(<><DisplayedNotifications profileKey="Customer:one"/><div aria-busy="false"><article data-notification-resource={id}>Displayed item</article></div></>);
 await waitFor(()=>expect(observed).toHaveLength(1));
 changed([{target:container.querySelector("article")!,isIntersecting:true} as unknown as IntersectionObserverEntry],{} as IntersectionObserver);
 await waitFor(()=>expect(api.writes[0]).toMatchObject({path:"/notifications/read-related",body:{displayedIds:[id]}}));
 const acknowledgement=api.fetch.mock.calls.find(call=>String(call[0]).endsWith("/notifications/read-related"))!;
 expect(new Headers(acknowledgement[1]?.headers).get("X-Weymela-Activity")).toBeNull();
 window.sessionStorage.setItem("weymela.profile-key","Creator:two");
 changed([{target:container.querySelector("article")!,isIntersecting:true} as unknown as IntersectionObserverEntry],{} as IntersectionObserver);
 await new Promise(resolve=>setTimeout(resolve,120));expect(api.writes).toHaveLength(1);
});

it("does not present a saved mobile-money recipient as an independently verified identity phone",async()=>{
 mockApi({"/creator/payout-destination":{method:"Telebirr",provider:"Telebirr",isConfigured:true,account:"+251911223344",registeredPhone:null,telebirrEligible:false,mpesaEligible:false,legalName:"Synthetic Creator"}});
 render(<MemoryRouter><PayoutDestinationForm endpoint="/creator/payout-destination"/></MemoryRouter>);
 await waitFor(()=>expect(screen.getByLabelText("Receiving phone number")).toHaveValue("+251911223344"));
 expect(screen.getByLabelText("Receiving phone number")).not.toHaveAttribute("readonly");
 expect(screen.getByRole("button",{name:"Save Destination"})).toBeEnabled();
 expect(screen.queryByText(/phone needs independent ownership verification/)).not.toBeInTheDocument();
 expect(screen.queryByLabelText("Verified Weymela phone")).not.toBeInTheDocument();
});
